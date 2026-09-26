using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Char;
using AAEmu.UnitTests.Utils.Mocks;

using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

[NotInParallel]
public class FavoritePortalPersistenceTests
{
    private static readonly FavoritePortalRef PrivateOne = new((byte)PortalBookType.Private, 1);
    private static readonly FavoritePortalRef PrivateTwo = new((byte)PortalBookType.Private, 2);
    private static readonly FavoritePortalRef ReturnTen = new((byte)PortalBookType.Return, 10);

    private static bool Owns(FavoritePortalRef favorite) =>
        favorite == PrivateOne || favorite == PrivateTwo || favorite == ReturnTen;

    [Test]
    public async Task SaveAndLiveUpdate_AreSerializedThroughThePersistenceWrite()
    {
        var owner = new CharacterMock();
        using var persistence = new RecordingPersistence { BlockSave = true };
        var favorites = new CharacterFavoritePortals(owner, persistence);
        favorites.LoadState([PrivateOne], Owns);

        var connection = Uninitialized<MySqlConnection>();
        var transaction = Uninitialized<MySqlTransaction>();
        var saveTask = Task.Run(() => favorites.Save(connection, transaction));
        await persistence.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var updateTask = Task.Run(() => favorites.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Private, 2, true)],
            Owns,
            int.MaxValue));
        await Task.Delay(50);
        persistence.ReleaseSave.TrySetResult(true);

        await saveTask;
        await Assert.That(updateTask.Result).IsTrue();
        await Assert.That(persistence.Writes.Count).IsEqualTo(2);
        var writes = persistence.Writes.Select(write => write.Select(item => item.PortalId).ToArray()).ToArray();
        await Assert.That(writes[0].Length).IsEqualTo(1);
        await Assert.That(writes[0][0]).IsEqualTo(1u);
        await Assert.That(writes[1].Length).IsEqualTo(2);
        await Assert.That(writes[1][0]).IsEqualTo(1u);
        await Assert.That(writes[1][1]).IsEqualTo(2u);
        await Assert.That(favorites.Contains(PrivateTwo)).IsTrue();
    }

    [Test]
    public async Task ImmediateUpdate_WaitsUntilOuterSaveReleasesPersistenceGate()
    {
        var owner = new CharacterMock();
        using var persistence = new RecordingPersistence();
        var favorites = new CharacterFavoritePortals(owner, persistence);
        favorites.LoadState([PrivateOne], Owns);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> updateTask;
        bool completedWhileSaveHeld;

        PersistenceGate.EnterSave();
        try
        {
            updateTask = Task.Run(() =>
            {
                started.TrySetResult(true);
                return favorites.TryApply(
                    [new FavoritePortalChange((byte)PortalBookType.Private, 2, true)],
                    Owns,
                    int.MaxValue);
            });
            started.Task.GetAwaiter().GetResult();
            completedWhileSaveHeld = SpinWait.SpinUntil(() => updateTask.IsCompleted, 100);
        }
        finally
        {
            PersistenceGate.ExitSave();
        }

        await Assert.That(completedWhileSaveHeld).IsFalse();
        await Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(5))).IsTrue();
        await Assert.That(persistence.PersistNowCalls).IsEqualTo(1);
    }

    [Test]
    public void ImmediatePersistence_RefusesOutsideOperationGate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new MySqlFavoritePortalPersistence().PersistNow(0, Array.Empty<FavoritePortalRef>()));
    }

    [Test]
    public async Task ImmediatePersistFailure_RollsBackStateAndAllowsRetry()
    {
        var owner = new CharacterMock();
        using var persistence = new RecordingPersistence { FailPersistNow = true };
        var favorites = new CharacterFavoritePortals(owner, persistence);
        favorites.LoadState([PrivateOne], Owns);

        var rejected = favorites.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Private, 2, true)],
            Owns,
            int.MaxValue);
        await Assert.That(rejected).IsFalse();
        await Assert.That(favorites.Contains(PrivateTwo)).IsFalse();

        persistence.FailPersistNow = false;
        var accepted = favorites.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Private, 2, true)],
            Owns,
            int.MaxValue);
        await Assert.That(accepted).IsTrue();
        await Assert.That(favorites.Contains(PrivateTwo)).IsTrue();
    }

    [Test]
    public async Task SaveFailure_RelesesUpdateLockWithoutChangingState()
    {
        var owner = new CharacterMock();
        using var persistence = new RecordingPersistence { FailSave = true };
        var favorites = new CharacterFavoritePortals(owner, persistence);
        favorites.LoadState([PrivateOne], Owns);

        Assert.Throws<InvalidOperationException>(() => favorites.Save(
            Uninitialized<MySqlConnection>(),
            Uninitialized<MySqlTransaction>()));

        var updated = favorites.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Return, 10, true)],
            Owns,
            int.MaxValue);
        await Assert.That(updated).IsTrue();
        await Assert.That(favorites.Contains(ReturnTen)).IsTrue();
    }

    [Test]
    public async Task RelogRestore_ReappliesFlagsWithoutChangingPortalOrder()
    {
        var owner = new CharacterMock();
        using var persistence = new RecordingPersistence();
        var favorites = new CharacterFavoritePortals(owner, persistence);
        favorites.LoadState([PrivateTwo, ReturnTen], Owns);
        var portals = new[]
        {
            new Portal { Id = 1, Name = "one", IsFavorite = true },
            new Portal { Id = 2, Name = "two" },
            new Portal { Id = 3, Name = "three" }
        };

        var restored = favorites.BuildFlaggedPortals(portals, PortalBookType.Private);

        await Assert.That(restored.Select(portal => portal.Id)).IsEquivalentTo(new uint[] { 1, 2, 3 });
        await Assert.That(restored[0].IsFavorite).IsFalse();
        await Assert.That(restored[1].IsFavorite).IsTrue();
        await Assert.That(restored[2].IsFavorite).IsFalse();
    }

    [Test]
    public async Task DeletionMarkers_SurviveRollbackAndCommitOnlyTheirSaveVersion()
    {
        var owner = new CharacterMock();
        owner.Portals = new CharacterPortals(owner);
        owner.Portals.PrivatePortals[1] = new Portal { Id = 1, SubZoneId = 1 };
        owner.Portals.PrivatePortals[2] = new Portal { Id = 2, SubZoneId = 2 };
        owner.Portals.FavoritePortals.LoadState([PrivateOne, PrivateTwo], Owns);

        owner.Portals.RemoveFromBookPortal(owner.Portals.PrivatePortals[1], true);
        var savedVersion = owner.Portals.PendingDeletionVersion;
        owner.Portals.RemoveFromBookPortal(owner.Portals.PrivatePortals[2], true);

        // A rollback performs no confirmation, so both deletions remain pending.
        await Assert.That(owner.Portals.PendingDeletedPortalCount).IsEqualTo(2);
        await Assert.That(owner.Portals.FavoritePortals.Contains(PrivateOne)).IsFalse();
        await Assert.That(owner.Portals.FavoritePortals.Contains(PrivateTwo)).IsFalse();

        // Committing the older save clears only the deletion it contained.
        owner.Portals.OnSaveCommitted(savedVersion);
        await Assert.That(owner.Portals.PendingDeletedPortalCount).IsEqualTo(1);
        await Assert.That(owner.Portals.HasPendingDeletedPortal(1)).IsFalse();
        await Assert.That(owner.Portals.HasPendingDeletedPortal(2)).IsTrue();

        owner.Portals.OnSaveCommitted(owner.Portals.PendingDeletionVersion);
        await Assert.That(owner.Portals.PendingDeletedPortalCount).IsEqualTo(0);
    }

    [Test]
    public async Task DistrictReAdd_WaitsForSaveAndKeepsRetryMarkerUntilCommitGateReleases()
    {
        var owner = new CharacterMock();
        owner.Portals = new CharacterPortals(owner);
        var district = new VisitedDistrict { Id = 1, SubZone = 7, Owner = owner.Id };
        owner.Portals.RestoreVisitedDistrictRecord(district);
        owner.Portals.RemoveVisitedDistrictRecord(district.SubZone);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task restoreTask;
        bool completedWhileSaveHeld;
        int pendingWhileSaveHeld;
        bool presentWhileSaveHeld;

        PersistenceGate.EnterSave();
        try
        {
            restoreTask = Task.Run(() =>
            {
                started.TrySetResult(true);
                owner.Portals.RestoreVisitedDistrictRecord(district);
            });
            started.Task.GetAwaiter().GetResult();
            completedWhileSaveHeld = SpinWait.SpinUntil(() => restoreTask.IsCompleted, 100);
            pendingWhileSaveHeld = owner.Portals.PendingDeletedDistrictCount;
            presentWhileSaveHeld = owner.Portals.HasVisitedDistrictRecord(district.SubZone);
        }
        finally
        {
            PersistenceGate.ExitSave();
        }

        await restoreTask.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(completedWhileSaveHeld).IsFalse();
        await Assert.That(pendingWhileSaveHeld).IsEqualTo(1);
        await Assert.That(presentWhileSaveHeld).IsFalse();
        await Assert.That(owner.Portals.PendingDeletedDistrictCount).IsEqualTo(0);
        await Assert.That(owner.Portals.HasVisitedDistrictRecord(district.SubZone)).IsTrue();
    }

    [Test]
    public async Task RejectedOwnershipUpdate_ResendsAuthoritativePortalState()
    {
        var owner = new CharacterMock();
        var syncCount = 0;
        var portals = new RecordingCharacterPortals(owner, () => syncCount++);
        portals.PrivatePortals[1] = new Portal { Id = 1, IsFavorite = false };

        var accepted = portals.TryUpdateFavorites(
            [new FavoritePortalChange((byte)PortalBookType.Private, 99, true)],
            1);

        await Assert.That(accepted).IsFalse();
        await Assert.That(syncCount).IsEqualTo(1);
    }

    [Test]
    public async Task FailedCharacterSave_DoesNotPublishCommitToken()
    {
        var coordinator = new PortalSaveCommitCoordinator();

        var token = coordinator.Complete(1, false);
        var confirmed = coordinator.Confirm(token);

        await Assert.That(token).IsNull();
        await Assert.That(confirmed).IsFalse();
    }

    [Test]
    public async Task CommitOrdering_OlderTokenCannotConfirmAfterNewerSavePublishes()
    {
        var coordinator = new PortalSaveCommitCoordinator();
        var older = coordinator.Complete(1, true);
        var newer = coordinator.Complete(2, true);

        var olderConfirmed = coordinator.Confirm(older);
        var newerConfirmed = coordinator.Confirm(newer);
        var duplicateConfirmed = coordinator.Confirm(newer);

        await Assert.That(older).IsNotNull();
        await Assert.That(newer).IsNotNull();
        await Assert.That(olderConfirmed).IsFalse();
        await Assert.That(newerConfirmed).IsTrue();
        await Assert.That(duplicateConfirmed).IsFalse();
    }

    [Test]
    public async Task HeroSaveRule_StopsAndPublishesNoTokenWhenCharacterSaveReturnsFalse()
    {
        var attempts = 0;

        var saved = HeroCharacterSaveRules.TrySave(
            (out PortalSaveCommitToken? token) =>
            {
                attempts++;
                token = null;
                return false;
            },
            out var publishedToken);

        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(saved).IsFalse();
        await Assert.That(publishedToken).IsNull();
    }

    [Test]
    public void Capacity_MissingOrDuplicateBonusRecordFailsLoudly()
    {
        var characterRecords = new CharacterRecords(new CharacterMock());
        var first = new CharRecords { Id = 101 };
        var second = new CharRecords { Id = 102 };

        Assert.Throws<InvalidOperationException>(() =>
            FavoritePortalCapacityRules.ResolveBonus(characterRecords, []));
        Assert.Throws<InvalidOperationException>(() =>
            FavoritePortalCapacityRules.ResolveBonus(characterRecords, [first, second]));
    }

    [Test]
    public async Task Capacity_CombinesBaseAndResolvedBonus()
    {
        var limit = FavoritePortalCapacityRules.CombineLimit(4, 2);

        await Assert.That(limit).IsEqualTo(6);
    }

    [Test]
    public async Task Capacity_ZeroAndNonzeroRecordBonusesAreRead()
    {
        var characterRecords = new CharacterRecords(new CharacterMock());
        var definition = new CharRecords { Id = 101 };
        var definitions = new[] { definition };

        var zero = FavoritePortalCapacityRules.ResolveBonus(characterRecords, definitions);
        characterRecords.Set(definition.Id, 3);
        var nonzero = FavoritePortalCapacityRules.ResolveBonus(characterRecords, definitions);

        await Assert.That(zero).IsEqualTo(0u);
        await Assert.That(nonzero).IsEqualTo(3u);
    }

    private static T Uninitialized<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class RecordingCharacterPortals(Character owner, Action onSync)
        : CharacterPortals(owner)
    {
        protected override void SendAuthoritativeState() => onSync();
    }

    private sealed class RecordingPersistence : IFavoritePortalPersistence, IDisposable
    {
        public ConcurrentQueue<IReadOnlyList<FavoritePortalRef>> Writes { get; } = new();
        public TaskCompletionSource<bool> SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockSave { get; init; }
        public bool FailPersistNow { get; set; }
        public bool FailSave { get; init; }
        public int PersistNowCalls => _persistNowCalls;

        private int _persistNowCalls;

        public void PersistNow(uint ownerId, IReadOnlyList<FavoritePortalRef> favorites)
        {
            Interlocked.Increment(ref _persistNowCalls);
            if (FailPersistNow)
                throw new InvalidOperationException("persist failed");
            Writes.Enqueue(favorites.ToArray());
        }

        public void Save(
            uint ownerId,
            MySqlConnection connection,
            MySqlTransaction transaction,
            IReadOnlyList<FavoritePortalRef> favorites)
        {
            SaveEntered.TrySetResult(true);
            if (BlockSave)
                ReleaseSave.Task.GetAwaiter().GetResult();
            if (FailSave)
                throw new InvalidOperationException("save failed");
            Writes.Enqueue(favorites.ToArray());
        }

        public void Dispose()
        {
        }
    }
}
