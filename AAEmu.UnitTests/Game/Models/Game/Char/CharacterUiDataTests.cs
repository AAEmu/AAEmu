using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterUiDataTests
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(15);
    private static readonly ushort[] SupportedSections = [1, 2, 3, 4, 5, 6, 7, 20];

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(20)]
    public async Task TrySaveUiData_FailurePreservesOldValue_IdenticalRetryCommits(int section)
    {
        var key = (ushort)section;
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        character.SetOption(key, "old");
        var valuesDuringSave = new List<string>();
        store.BeforeSave = (_, _, _) =>
        {
            // The callback is reentrant: memory must still contain the old value at commit time.
            valuesDuringSave.Add(character.GetOption(key));
            if (valuesDuringSave.Count == 1)
                throw new InvalidOperationException("Synthetic persistence failure");
        };

        await Assert.That(character.TrySaveUiData(key, "replacement")).IsFalse();
        await Assert.That(character.GetOption(key)).IsEqualTo("old");
        await Assert.That(store.Saved).IsEmpty();

        await Assert.That(character.TrySaveUiData(key, "replacement")).IsTrue();
        await Assert.That(character.GetOption(key)).IsEqualTo("replacement");
        await Assert.That(valuesDuringSave.SequenceEqual(new[] { "old", "old" })).IsTrue();
        await Assert.That(store.Attempts.Count).IsEqualTo(2);
        await Assert.That(store.Attempts.All(write => write == (42u, key, "replacement"))).IsTrue();
        await Assert.That(store.Saved.Single()).IsEqualTo((42u, key, "replacement"));
    }

    [Test]
    public async Task TrySaveUiData_InactiveCharacterNeedsNoConnection_LeavesUnknownFieldsUntouched()
    {
        const string payload = "  {\"syntheticVersion\":73,\"unknownFutureField\":{\"x\":[3,null,\"a\\\\b\"]}}\r\n";
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };

        await Assert.That(character.Connection).IsNull();
        await Assert.That(character.IsOnline).IsFalse();
        await Assert.That(character.TrySaveUiData(20, payload)).IsTrue();

        await Assert.That(character.GetOption(20)).IsEqualTo(payload);
        await Assert.That(store.Saved.Single()).IsEqualTo((42u, (ushort)20, payload));
        await Assert.That(character.Connection).IsNull();
        await Assert.That(character.IsOnline).IsFalse();
    }

    [Test]
    public async Task TrySaveUiData_IsolatesCharactersAndSections()
    {
        var store = new FakeOptionStore();
        var first = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        var second = new Character(new UnitCustomModelParams(), store) { Id = 43 };

        await Assert.That(first.GetOption(1)).IsEqualTo("");
        await Assert.That(second.GetOption(1)).IsEqualTo("");
        foreach (var key in SupportedSections)
        {
            await Assert.That(first.TrySaveUiData(key, $"first-{key}")).IsTrue();
            await Assert.That(second.TrySaveUiData(key, $"second-{key}")).IsTrue();
        }
        await Assert.That(first.TrySaveUiData(1, "updated")).IsTrue();

        foreach (var key in SupportedSections)
        {
            var expected = key == 1 ? "updated" : $"first-{key}";
            await Assert.That(first.GetOption(key)).IsEqualTo(expected);
            await Assert.That(second.GetOption(key)).IsEqualTo($"second-{key}");
            await Assert.That(store.Saved.Last(write => write.CharacterId == 42 && write.Key == key).Value)
                .IsEqualTo(expected);
            await Assert.That(store.Saved.Single(write => write.CharacterId == 43 && write.Key == key).Value)
                .IsEqualTo($"second-{key}");
        }
        await Assert.That(store.Saved.Count).IsEqualTo(17);
    }

    [Test]
    public async Task TrySaveUiData_UnsupportedSectionsNeverReachStoreOrChangeMemory()
    {
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        ushort[] unsupported = [0, 8, 19, 21, 0xE400, 0xE4FF, ushort.MaxValue];

        foreach (var key in unsupported)
        {
            character.SetOption(key, "old");
            await Assert.That(character.TrySaveUiData(key, "replacement")).IsFalse();
            await Assert.That(character.GetOption(key)).IsEqualTo("old");
        }
        await Assert.That(store.Attempts).IsEmpty();
    }

    [Test]
    public async Task TrySaveUiData_InvalidTextNeverReachesStoreOrChangesMemory()
    {
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        character.SetOption(1, "old");
        string[] invalid =
        [
            null, "embedded\0nul", "\uD800", "\uDC00", "\uD800x",
            new string('a', 8192), new string('\u00E9', 4096),
            string.Concat(Enumerable.Repeat("\uD83D\uDE00", 2048))
        ];

        foreach (var payload in invalid)
        {
            await Assert.That(character.TrySaveUiData(1, payload)).IsFalse();
            await Assert.That(character.GetOption(1)).IsEqualTo("old");
        }
        await Assert.That(store.Attempts).IsEmpty();
    }

    [Test]
    public async Task TrySaveUiData_AcceptsEmptyAndStrictUtf8At8191ByteBoundary()
    {
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        string[] valid =
        [
            "", new string('a', 8191), new string('\u00E9', 4095) + "a",
            string.Concat(Enumerable.Repeat("\uD83D\uDE00", 2047)) + "abc"
        ];

        foreach (var payload in valid)
        {
            await Assert.That(character.TrySaveUiData(1, payload)).IsTrue();
            await Assert.That(character.GetOption(1)).IsEqualTo(payload);
        }
        await Assert.That(store.Saved.Select(write => write.Value).SequenceEqual(valid)).IsTrue();
    }

    [Test]
    public async Task TrySaveUiData_ConsecutiveSavesCommitInOrder_IncludingRepeatedAndEmptyPayloads()
    {
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        character.SetOption(1, "seed");
        var observed = new List<string>();
        store.BeforeSave = (_, key, _) => observed.Add(character.GetOption(key));
        string[] payloads = ["first", "second", "second", ""];

        foreach (var payload in payloads)
        {
            await Assert.That(character.TrySaveUiData(1, payload)).IsTrue();
            await Assert.That(character.GetOption(1)).IsEqualTo(payload);
        }

        await Assert.That(observed.SequenceEqual(new[] { "seed", "first", "second", "second" })).IsTrue();
        await Assert.That(store.Saved.Select(write => write.Value).SequenceEqual(payloads)).IsTrue();
    }

    [Test]
    public async Task GetOptionsForSave_ExcludesEveryUiSection_PreservesAllContentToggleKeys()
    {
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        foreach (var key in SupportedSections)
            character.SetOption(key, $"ui-{key}");

        var expected = new Dictionary<ushort, string>();
        for (var content = 0; content <= byte.MaxValue; content++)
            expected[(ushort)(0xE400 + content)] = content % 2 == 0 ? "0" : "1";
        foreach (ushort key in new ushort[] { 0, 8, 19, 21, ushort.MaxValue })
            expected[key] = $"non-ui-{key}";
        foreach (var pair in expected)
            character.SetOption(pair.Key, pair.Value);

        var snapshot = character.GetOptionsForSave();

        await Assert.That(snapshot.OrderBy(pair => pair.Key).SequenceEqual(expected.OrderBy(pair => pair.Key))).IsTrue();
        foreach (var key in SupportedSections)
            await Assert.That(character.GetOption(key)).IsEqualTo($"ui-{key}");
        await Assert.That(store.Attempts).IsEmpty();
    }

    [Test]
    public async Task GetOptionsForSave_SnapshotsAreIndependentOfLaterSavesAndMutations()
    {
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        character.SetOption(1, "old-ui");
        character.SetOption(0xE400, "0");
        var first = character.GetOptionsForSave();
        var second = character.GetOptionsForSave();

        character.SetOption(0xE400, "1");
        character.SetOption(0xE401, "added");
        await Assert.That(character.TrySaveUiData(1, "new-ui")).IsTrue();

        await Assert.That(first).HasSingleItem();
        await Assert.That(first[0]).IsEqualTo(new KeyValuePair<ushort, string>(0xE400, "0"));
        first[0] = new KeyValuePair<ushort, string>(1, "snapshot-only");

        await Assert.That(second).HasSingleItem();
        await Assert.That(second[0]).IsEqualTo(new KeyValuePair<ushort, string>(0xE400, "0"));
        await Assert.That(character.GetOption(1)).IsEqualTo("new-ui");
        await Assert.That(character.GetOption(0xE400)).IsEqualTo("1");
        var latest = character.GetOptionsForSave().ToDictionary(pair => pair.Key, pair => pair.Value);
        await Assert.That(latest.Count).IsEqualTo(2);
        await Assert.That(latest[0xE400]).IsEqualTo("1");
        await Assert.That(latest[0xE401]).IsEqualTo("added");
        await Assert.That(store.Saved.Single()).IsEqualTo((42u, (ushort)1, "new-ui"));
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PendingSave_SerializesWriters_WithoutBlockingOtherCharacters(int section)
    {
        var secondKey = (ushort)section;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        var otherCharacter = new Character(new UnitCustomModelParams(), store) { Id = 43 };
        character.SetOption(1, "old");
        character.SetOption(0xE400, "toggle");
        store.BeforeSave = (characterId, _, value) =>
        {
            if (characterId != 42 || value != "first")
                return;
            entered.Set();
            if (!release.Wait(CoordinationTimeout * 3))
                throw new TimeoutException("Test did not release the pending save");
        };

        var first = Task.Factory.StartNew(() => character.TrySaveUiData(1, "first"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                started.SetResult();
                completed.SetResult(character.TrySaveUiData(secondKey, "second"));
            }
            catch (Exception exception)
            {
                completed.SetException(exception);
            }
        }) { IsBackground = true };
        var workerStarted = false;

        try
        {
            await Assert.That(entered.Wait(CoordinationTimeout)).IsTrue();
            worker.Start();
            workerStarted = true;
            await started.Task.WaitAsync(CoordinationTimeout);

            // This worker can only wait on the writer lock. Observe contention, not elapsed time.
            var settled = SpinWait.SpinUntil(() => completed.Task.IsCompleted ||
                (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, CoordinationTimeout);
            await Assert.That(settled).IsTrue();
            await Assert.That(completed.Task.IsCompleted).IsFalse();
            await Assert.That(store.Attempts.Count).IsEqualTo(1);
            await Assert.That(store.Saved).IsEmpty();

            var otherSave = Task.Run(() => otherCharacter.TrySaveUiData(1, "independent"));
            await Assert.That(await otherSave.WaitAsync(CoordinationTimeout)).IsTrue();
            await Assert.That(otherCharacter.GetOption(1)).IsEqualTo("independent");
            await Assert.That(store.Saved.Single()).IsEqualTo((43u, (ushort)1, "independent"));
        }
        finally
        {
            release.Set();
            await first.WaitAsync(CoordinationTimeout);
            if (workerStarted)
                await completed.Task.WaitAsync(CoordinationTimeout);
        }

        await Assert.That(await first).IsTrue();
        await Assert.That(await completed.Task).IsTrue();
        await Assert.That(character.GetOption(1)).IsEqualTo(secondKey == 1 ? "second" : "first");
        await Assert.That(character.GetOption(secondKey)).IsEqualTo("second");
        var writes = store.Saved.Where(write => write.CharacterId == 42).ToArray();
        await Assert.That(writes.Length).IsEqualTo(2);
        await Assert.That(writes[0]).IsEqualTo((42u, (ushort)1, "first"));
        await Assert.That(writes[1]).IsEqualTo((42u, secondKey, "second"));
    }

    [Test]
    public async Task AutosaveHoldingLastPooledConnection_CanReadSeedAndSnapshotWhileUiSaveWaits()
    {
        using var pool = new SemaphoreSlim(1, 1);
        using var acquiringConnection = new ManualResetEventSlim();
        var store = new FakeOptionStore();
        var character = new Character(new UnitCustomModelParams(), store) { Id = 42 };
        character.SetOption(1, "old-ui");
        character.SetOption(0xE400, "0");
        store.BeforeSave = (_, _, _) =>
        {
            acquiringConnection.Set();
            if (!pool.Wait(CoordinationTimeout * 3))
                throw new TimeoutException("Autosave did not release the simulated connection");
            pool.Release();
        };

        // Autosave owns the last connection before the UI writer tries to acquire one.
        await Assert.That(await pool.WaitAsync(CoordinationTimeout)).IsTrue();
        var writer = Task.Factory.StartNew(() => character.TrySaveUiData(1, "new-ui"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task cacheAccess = Task.CompletedTask;
        try
        {
            await Assert.That(acquiringConnection.Wait(CoordinationTimeout)).IsTrue();
            var autosave = Task.Factory.StartNew(() =>
            {
                var before = character.GetOption(1);
                character.SetOption(2, "loaded-ui");
                character.SetOption(0xE400, "1");
                return (Before: before, After: character.GetOption(1), Seeded: character.GetOption(2),
                    Snapshot: character.GetOptionsForSave());
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            cacheAccess = autosave;

            // These operations must finish while autosave still holds the permit, not after I/O.
            var result = await autosave.WaitAsync(CoordinationTimeout);
            await Assert.That(result.Before).IsEqualTo("old-ui");
            await Assert.That(result.After).IsEqualTo("old-ui");
            await Assert.That(result.Seeded).IsEqualTo("loaded-ui");
            await Assert.That(result.Snapshot).HasSingleItem();
            await Assert.That(result.Snapshot[0]).IsEqualTo(new KeyValuePair<ushort, string>(0xE400, "1"));
            await Assert.That(writer.IsCompleted).IsFalse();
            await Assert.That(store.Attempts.Single()).IsEqualTo((42u, (ushort)1, "new-ui"));
            await Assert.That(store.Saved).IsEmpty();
        }
        finally
        {
            // Also break the simulated inversion on failure so no worker is left blocked.
            pool.Release();
            await Task.WhenAll(writer, cacheAccess).WaitAsync(CoordinationTimeout);
        }

        await Assert.That(await writer).IsTrue();
        await Assert.That(character.GetOption(1)).IsEqualTo("new-ui");
        await Assert.That(character.GetOption(2)).IsEqualTo("loaded-ui");
        await Assert.That(character.GetOption(0xE400)).IsEqualTo("1");
        await Assert.That(store.Saved.Single()).IsEqualTo((42u, (ushort)1, "new-ui"));
    }

    private sealed class FakeOptionStore : ICharacterOptionStore
    {
        public ConcurrentQueue<(uint CharacterId, ushort Key, string Value)> Attempts { get; } = new();
        public ConcurrentQueue<(uint CharacterId, ushort Key, string Value)> Saved { get; } = new();
        public Action<uint, ushort, string> BeforeSave { get; set; }

        public void Save(uint characterId, ushort key, string value)
        {
            Attempts.Enqueue((characterId, key, value));
            BeforeSave?.Invoke(characterId, key, value);
            Saved.Enqueue((characterId, key, value));
        }
    }
}
