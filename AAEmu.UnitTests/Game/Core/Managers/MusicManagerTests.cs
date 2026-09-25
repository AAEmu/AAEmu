using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Music;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MusicManagerTests
{
    private const uint PlayerId = 11;
    private const uint OtherPlayerId = 12;
    private const ulong ScoreItemId = 9001;

    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockMusicId = Mock.Of<IMusicIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var manager = new MusicManager(mockMusicId.Object, mockItem.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockMusicId);
        Mock.VerifyNoOtherCalls(mockItem);
    }

    [Test]
    public async Task MidiCache_FailedReplacementClearsThePreviousBlock()
    {
        var manager = CreateManager(null);
        await Assert.That(manager.CacheMidi(PlayerId, [0x4D, 0x54])).IsTrue();

        await Assert.That(manager.CacheMidi(PlayerId, null)).IsFalse();
        await Assert.That(manager.TryGetMidiCache(PlayerId, out var missing)).IsFalse();
        await Assert.That(missing).IsNull();

        await Assert.That(manager.CacheMidi(PlayerId, [])).IsFalse();
        await Assert.That(manager.TryGetMidiCache(PlayerId, out missing)).IsFalse();
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task MidiCache_ReplacesOnlyWithAValidBlock()
    {
        var manager = CreateManager(null);
        var first = new byte[] { 0x4D, 0x54 };
        var second = new byte[] { 0x68, 0x64 };

        await Assert.That(manager.CacheMidi(PlayerId, first)).IsTrue();
        await Assert.That(manager.TryGetMidiCache(PlayerId, out var cached)).IsTrue();
        await Assert.That(cached).IsEquivalentTo(first);

        await Assert.That(manager.CacheMidi(PlayerId, second)).IsTrue();
        await Assert.That(manager.TryGetMidiCache(PlayerId, out cached)).IsTrue();
        await Assert.That(cached).IsEquivalentTo(second);
    }

    [Test]
    public async Task LogoutEntryPoint_IsOneBaseUnitOverloadSoTheU02PathCannotBeShadowed()
    {
        var overloads = typeof(MusicManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(MusicManager.OnCharacterLogout))
            .ToArray();

        await Assert.That(overloads.Length).IsEqualTo(1);
        await Assert.That(overloads[0].GetParameters().Single().ParameterType).IsEqualTo(typeof(BaseUnit));
    }

    [Test]
    public async Task MidiCache_LogoutInvalidationClearsOnlyThatPlayer()
    {
        var manager = CreateManager(null);
        await Assert.That(manager.CacheMidi(PlayerId, [0x4D, 0x54])).IsTrue();
        await Assert.That(manager.CacheMidi(OtherPlayerId, [0x68, 0x64])).IsTrue();

        manager.OnCharacterLogout(new CharacterMock { Id = PlayerId });

        await Assert.That(manager.TryGetMidiCache(PlayerId, out _)).IsFalse();
        await Assert.That(manager.TryGetMidiCache(OtherPlayerId, out var other)).IsTrue();
        await Assert.That(other).IsEquivalentTo(new byte[] { 0x68, 0x64 });
    }

    [Test]
    public async Task MidiCache_ConcurrentReplacementAndReadsRemainSafe()
    {
        var manager = CreateManager(null);
        var payloads = Enumerable.Range(0, 64)
            .Select(index => new[] { (byte)index, (byte)(index + 1), (byte)(index + 2) })
            .ToArray();

        Parallel.ForEach(payloads, payload =>
        {
            manager.CacheMidi(PlayerId, payload);
            manager.TryGetMidiCache(PlayerId, out _);
        });

        await Assert.That(manager.TryGetMidiCache(PlayerId, out var cached)).IsTrue();
        await Assert.That(payloads.Any(payload => payload.SequenceEqual(cached))).IsTrue();
    }

    [Test]
    public async Task UploadSong_QueuesTheNotesOfAnOwnedScoreItem()
    {
        var manager = CreateManager(OwnedScoreItem());

        await Assert.That(manager.UploadSong(PlayerId, "My Song", "c1 d2", ScoreItemId)).IsTrue();

        var queued = QueuedSong(manager, PlayerId);
        await Assert.That(queued).IsNotNull();
        await Assert.That(queued.Title).IsEqualTo("My Song");
        await Assert.That(queued.Song).IsEqualTo("c1 d2");
        await Assert.That(queued.AuthorId).IsEqualTo(PlayerId);
        await Assert.That(queued.SourceItemId).IsEqualTo(ScoreItemId);
    }

    [Test]
    public async Task UploadSong_RefusesAnItemThatIsNotOwned()
    {
        var manager = CreateManager(null);

        await Assert.That(manager.UploadSong(PlayerId, "My Song", "c1 d2", ScoreItemId)).IsFalse();
        await Assert.That(QueuedSong(manager, PlayerId)).IsNull();
    }

    [Test]
    public async Task UploadSong_RefusesSomebodyElsesItem()
    {
        var item = OwnedScoreItem();
        item.OwnerId = OtherPlayerId;
        var manager = CreateManager(item);

        await Assert.That(manager.UploadSong(PlayerId, "My Song", "c1 d2", ScoreItemId)).IsFalse();
        await Assert.That(QueuedSong(manager, PlayerId)).IsNull();
    }

    [Test]
    public async Task UploadSong_RefusesNotesThatDoNotFitTheCompositionLimit()
    {
        var manager = CreateManager(OwnedScoreItem());

        await Assert.That(manager.MaxNoteBytes).IsEqualTo(MusicNoteRules.DefaultMaxNoteBytes);
        await Assert.That(manager.UploadSong(PlayerId, "My Song", new string('a', 5001), ScoreItemId)).IsFalse();
        await Assert.That(manager.UploadSong(PlayerId, string.Empty, "c1", ScoreItemId)).IsFalse();
        await Assert.That(QueuedSong(manager, PlayerId)).IsNull();
    }

    [Test]
    public async Task UploadSong_ReplacesThePreviousUploadOfThatPlayer()
    {
        var manager = CreateManager(OwnedScoreItem());

        await Assert.That(manager.UploadSong(PlayerId, "First", "c1", ScoreItemId)).IsTrue();
        await Assert.That(manager.UploadSong(PlayerId, "Second", "d2", ScoreItemId)).IsTrue();

        var queued = QueuedSong(manager, PlayerId);
        await Assert.That(queued.Title).IsEqualTo("Second");
        await Assert.That(queued.Song).IsEqualTo("d2");
    }

    [Test]
    public async Task UploadSong_KeepsPlayersApart()
    {
        var item = OwnedScoreItem();
        var otherItem = OwnedScoreItem();
        otherItem.OwnerId = OtherPlayerId;
        var manager = CreateManager(item, (ScoreItemId + 1, otherItem));

        await Assert.That(manager.UploadSong(PlayerId, "Mine", "c1", ScoreItemId)).IsTrue();
        await Assert.That(manager.UploadSong(OtherPlayerId, "Theirs", "d2", ScoreItemId + 1)).IsTrue();

        await Assert.That(QueuedSong(manager, PlayerId).Title).IsEqualTo("Mine");
        await Assert.That(QueuedSong(manager, OtherPlayerId).Title).IsEqualTo("Theirs");
    }

    [Test]
    public async Task CreateSheetMusic_RefusesAnItemThatIsNotHeldByThePlayer()
    {
        var manager = CreateManager(OwnedScoreItem());
        var player = new CharacterMock { Id = PlayerId, Name = "Tester" };
        DetachedInventory.Create(player);
        var loose = new ItemMock((uint)ScoreItemId, 27902); // never placed in a container

        await Assert.That(manager.CreateSheetMusic(player, loose)).IsFalse();
        await Assert.That(manager.CreateSheetMusic(player, null)).IsFalse();
    }

    [Test]
    public async Task CreateSheetMusic_RefusesABlankScoreWithoutComposedNotes()
    {
        var manager = CreateManager(null);
        var player = new CharacterMock { Id = PlayerId, Name = "Tester" };
        var inventory = DetachedInventory.Create(player);
        var blank = new ItemMock((uint)ScoreItemId, 27902) { _holdingContainer = inventory.Bag };
        inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, blank);

        await Assert.That(manager.CreateSheetMusic(player, blank)).IsFalse();
    }

    private static MusicSheetItem OwnedScoreItem()
    {
        return new MusicSheetItem(ScoreItemId, new ItemTemplate { Id = 27902 }, 1) { OwnerId = PlayerId };
    }

    private static MusicManager CreateManager(Item item, params (ulong ItemId, Item Item)[] extraItems)
    {
        var mockMusicId = Mock.Of<IMusicIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        if (item != null)
            mockItem.GetItemByItemId(item.Id).Returns(item);
        foreach (var extra in extraItems)
            mockItem.GetItemByItemId(extra.ItemId).Returns(extra.Item);

        return new MusicManager(mockMusicId.Object, mockItem.Object);
    }

    private static SongData QueuedSong(MusicManager manager, uint playerId)
    {
        var field = typeof(MusicManager).GetField("_uploadQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        var queue = (Dictionary<uint, SongData>)field.GetValue(manager);
        return queue.TryGetValue(playerId, out var song) ? song : null;
    }
}
