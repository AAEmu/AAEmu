using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Collections;
using AAEmu.Game.Models.Game.Units;

using Microsoft.Extensions.DependencyInjection;

using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Models.Game.Collections;

/// <summary>
/// The collection/encyclopedia domain against seeded content: what the loaders accept and skip, what a
/// discovery does to a character, that completion and its reward happen once, and that the login sync
/// queues at load and replays once after world entry.
/// </summary>
[NotInParallel]
public sealed class CollectionsManagerTests : SqliteTestBase
{
    // Collection content: category 9 carries the collection kind, sub-category 36 sits under it.
    private const uint CollectionSubCategory = 36;
    private const uint GeneralSubCategory = 1;

    // Watch records over the item types they name.
    private const uint CollectGetRecord = 2001;
    private const uint CollectEquipRecord = 2002;
    private const uint CollectUnpackRecord = 2003;
    private const uint RecordOnlyItemRecord = 2004;
    private const uint GeneralGetRecord = 2005;

    // Item types.
    private const uint CollectItem = 70001;
    private const uint EquipItem = 70002;
    private const uint UnpackItem = 70003;
    private const uint RecordOnlyItem = 70004;
    private const uint EncyclopediaMember = 60001;
    private const uint DroppedMemberItem = 60002;
    private const uint DanglingMemberItem = 99999;
    private const uint NotAnEntryItem = 55555;

    private const uint EncyclopediaGuide = 500;
    private const uint CollectionRewardTitle = 9001;

    private readonly List<byte[]> _sentPackets = [];
    private Character _character;
    private CollectionGameData _collectionData;

    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("CREATE TABLE achievements (id INTEGER PRIMARY KEY, name TEXT, summary TEXT, description TEXT, " +
                "achievement_sub_category_id INTEGER, parent_achievement_id INTEGER, season_off TEXT, " +
                "is_hidden TEXT, priority INTEGER, or_unit_reqs TEXT, complete_or TEXT, complete_num INTEGER, " +
                "item_id INTEGER, icon_id INTEGER, item_num INTEGER, grade_id INTEGER, appellation_id INTEGER, " +
                "milestone_id INTEGER)");
        Execute("CREATE TABLE achievement_objectives (id INTEGER PRIMARY KEY, achievement_id INTEGER, " +
                "or_unit_reqs TEXT, record_id INTEGER)");
        Execute("CREATE TABLE char_records (id INTEGER PRIMARY KEY, kind_id INTEGER, value1 INTEGER, value2 INTEGER)");
        Execute("CREATE TABLE pre_completed_achievements (id INTEGER PRIMARY KEY, " +
                "completed_achievement_id INTEGER, my_achievement_id INTEGER)");
        Execute("CREATE TABLE enum_achievement_kinds (id INTEGER PRIMARY KEY, name TEXT)");
        Execute("CREATE TABLE achievement_categories (id INTEGER PRIMARY KEY, achievement_kind_id INTEGER)");
        Execute("CREATE TABLE achievement_sub_categories (id INTEGER PRIMARY KEY, achievement_category_id INTEGER)");
        Execute("CREATE TABLE items (id INTEGER PRIMARY KEY)");
        Execute("CREATE TABLE item_guide_impls (id INTEGER PRIMARY KEY)");
        Execute("CREATE TABLE item_guide_a_categories (id INTEGER PRIMARY KEY, item_guide_impl_id INTEGER)");
        Execute("CREATE TABLE item_guide_b_categories (id INTEGER PRIMARY KEY, item_guide_a_category_id INTEGER)");
        // Deliberately no primary key: duplicate content ids are data the loader has to survive.
        Execute("CREATE TABLE item_guides (id INTEGER, item_guide_impl_id INTEGER)");
        Execute("CREATE TABLE item_guide_elems (item_id INTEGER, item_guide_id INTEGER, " +
                "item_guide_a_category_id INTEGER, item_guide_b_category_id INTEGER)");
    }

    [Before(Test)]
    public void Before()
    {
        SeedContent();

        var achievementData = new AchievementGameData();
        achievementData.Load(Connection);
        achievementData.PostLoad();

        _collectionData = new CollectionGameData();
        _collectionData.Load(Connection);

        ResetSingleton<AchievementGameData>();
        ResetSingleton<AchievementManager>();
        ResetSingleton<CollectionGameData>();
        ResetSingleton<CollectionsManager>();
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(achievementData)
            .AddSingleton(_collectionData)
            .AddSingleton(new AchievementManager())
            .AddSingleton(new CollectionsManager())
            .BuildServiceProvider();

        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        var connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams()) { Id = 42, Name = "Collector" };
        _character.Connection = connection;
        connection.ActiveChar = _character;
        _character.Records = new CharacterRecords(_character);
        _character.Achievements = new CharacterAchievements(_character);
        _character.Collections = new CharacterCollections(_character);
        _character.Appellations = new CharacterAppellations(_character);
        // Mid-session by default: each test that models the load window flips this back itself.
        _character.WorldEntryCompleted = true;
    }

    [After(Test)]
    public void After()
    {
        SingletonContainer.ServiceProvider = null;
        ResetSingleton<AchievementGameData>();
        ResetSingleton<AchievementManager>();
        ResetSingleton<CollectionGameData>();
        ResetSingleton<CollectionsManager>();
    }

    // ---------------------------------------------------------------- content loading

    [Test]
    public async Task Load_KeepsValidRowsAndDropsDanglingEncyclopediaReferences()
    {
        // Guides with an unknown impl, members pointing at unknown guides/items/categories, and the
        // duplicate guide row are all data the shipped database can carry; none of them may load.
        await Assert.That(_collectionData.EncyclopediaGuideCount).IsEqualTo(1);
        await Assert.That(_collectionData.IsKnownEntry(EncyclopediaMember)).IsTrue();
        await Assert.That(_collectionData.IsKnownEntry(DanglingMemberItem)).IsFalse();
        await Assert.That(_collectionData.IsKnownEntry(DroppedMemberItem)).IsFalse();
        await Assert.That(_collectionData.IsKnownEntry(NotAnEntryItem)).IsFalse();

        // A record-only item is still a known entry even though its encyclopedia row was dropped.
        await Assert.That(_collectionData.IsKnownEntry(RecordOnlyItem)).IsTrue();

        await Assert.That(_collectionData.GetEncyclopediaEntries(EncyclopediaMember))
            .Contains(EncyclopediaGuide);
        await Assert.That(_collectionData.GetEncyclopediaEntries(EquipItem).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task Load_MembershipFollowsTheCollectionCategory()
    {
        await Assert.That(_collectionData.IsCollectionAchievement(100u)).IsTrue();
        await Assert.That(_collectionData.IsCollectionAchievement(101u)).IsTrue();
        await Assert.That(_collectionData.IsCollectionAchievement(102u)).IsTrue();
        // Achievement 50 watches the same item type but belongs to a general category.
        await Assert.That(_collectionData.IsCollectionAchievement(50u)).IsFalse();
        await Assert.That(_collectionData.CollectionAchievementIds.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Load_WatchRecordsResolvePerSourceEvent()
    {
        var acquired = _collectionData.GetRecordsToReport(CollectItem, CollectionDiscoverySource.Acquired);
        await Assert.That(acquired.Count).IsEqualTo(2);
        await Assert.That(acquired).Contains(CollectGetRecord);
        await Assert.That(acquired).Contains(GeneralGetRecord);
        await Assert.That(_collectionData.GetRecordsToReport(EquipItem, CollectionDiscoverySource.Equipped))
            .Contains(CollectEquipRecord);
        await Assert.That(_collectionData.GetRecordsToReport(UnpackItem, CollectionDiscoverySource.Unpacked))
            .Contains(CollectUnpackRecord);
        // The same item reports nothing on a source its content does not watch.
        await Assert.That(_collectionData.GetRecordsToReport(EquipItem,
                CollectionDiscoverySource.Acquired).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task Load_WithoutTheCollectionKindRow_FailsLoudly()
    {
        Execute("DELETE FROM enum_achievement_kinds WHERE name = 'collection'");

        var loader = new CollectionGameData();
        await Assert.That(() => loader.Load(Connection)).Throws<InvalidOperationException>();
    }

    // ---------------------------------------------------------------- discovery transitions

    [Test]
    public async Task Discover_UnknownEntryIsRejectedAndNotPersisted()
    {
        var result = CollectionsManager.Instance.Discover(_character, NotAnEntryItem,
            CollectionDiscoverySource.Acquired);

        await Assert.That(result).IsEqualTo(CollectionDiscoveryResult.UnknownEntry);
        await Assert.That(_character.Collections.Count).IsEqualTo(0);
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Discover_FirstAcquisitionPersistsMovesProgressAndCompletes()
    {
        var result = CollectionsManager.Instance.Discover(_character, CollectItem,
            CollectionDiscoverySource.Acquired);

        await Assert.That(result).IsEqualTo(CollectionDiscoveryResult.Discovered);
        await Assert.That(_character.Collections.IsDiscovered(CollectItem)).IsTrue();
        await Assert.That(_character.Collections.Count).IsEqualTo(1);

        // Both watch records over that item type moved — the collection one and the general one.
        await Assert.That(_character.Records.Get(CollectGetRecord)).IsEqualTo(1);
        await Assert.That(_character.Records.Get(GeneralGetRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(100u)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(50u)).IsTrue();

        // The reward title came with the completion, and the client heard the completion.
        await Assert.That(_character.Appellations.Appellations).Contains(CollectionRewardTitle);
        await Assert.That(SentOpcodes()).Contains(SCOffsets.SCAchievementCompletedPacket);
    }

    [Test]
    public async Task Discover_ReplayedDiscoveryNeitherDoubleCountsNorRepays()
    {
        CollectionsManager.Instance.Discover(_character, CollectItem,
            CollectionDiscoverySource.Acquired);
        _sentPackets.Clear();

        var replay = CollectionsManager.Instance.Discover(_character, CollectItem,
            CollectionDiscoverySource.Acquired);

        await Assert.That(replay).IsEqualTo(CollectionDiscoveryResult.AlreadyKnown);
        await Assert.That(_character.Collections.Count).IsEqualTo(1);
        // Records keep the high-water mark, so the replay moved nothing and pushed nothing.
        await Assert.That(_sentPackets.Count).IsEqualTo(0);

        // Claiming again — through the forced path as well as a re-report — must grant nothing more.
        await Assert.That(AchievementManager.Instance.Complete(_character, 100u)).IsFalse();
        AchievementManager.Instance.RefreshAll(_character);
        await Assert.That(_character.Appellations.Appellations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Discover_ReportsTheContentEventTheSourceNames()
    {
        // The equip watch does not move on a plain acquisition: the entry is discovered, but the
        // content record that watches equipping waits for the equip event.
        await Assert.That(CollectionsManager.Instance.Discover(_character, EquipItem,
                CollectionDiscoverySource.Acquired))
            .IsEqualTo(CollectionDiscoveryResult.Discovered);
        await Assert.That(_character.Records.Get(CollectEquipRecord)).IsEqualTo(0);
        await Assert.That(_character.Achievements.IsComplete(101u)).IsFalse();

        // The equip event arrives later for an entry discovered earlier and still progresses.
        await Assert.That(CollectionsManager.Instance.Discover(_character, EquipItem,
                CollectionDiscoverySource.Equipped))
            .IsEqualTo(CollectionDiscoveryResult.AlreadyKnown);
        await Assert.That(_character.Records.Get(CollectEquipRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(101u)).IsTrue();

        await Assert.That(CollectionsManager.Instance.Discover(_character, UnpackItem,
                CollectionDiscoverySource.Unpacked))
            .IsEqualTo(CollectionDiscoveryResult.Discovered);
        await Assert.That(_character.Records.Get(CollectUnpackRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(102u)).IsTrue();
    }

    [Test]
    public async Task Discover_MalformedRequestsAreRejected()
    {
        await Assert.That(CollectionsManager.Instance.Discover(null, CollectItem,
                CollectionDiscoverySource.Acquired))
            .IsEqualTo(CollectionDiscoveryResult.Rejected);
        await Assert.That(CollectionsManager.Instance.Discover(_character, 0,
                CollectionDiscoverySource.Acquired))
            .IsEqualTo(CollectionDiscoveryResult.Rejected);

        _character.Collections = null;
        await Assert.That(CollectionsManager.Instance.Discover(_character, CollectItem,
                CollectionDiscoverySource.Acquired))
            .IsEqualTo(CollectionDiscoveryResult.Rejected);
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    // ---------------------------------------------------------------- login sync

    [Test]
    public async Task InitialSync_QueuesAtLoadAndReplaysOnceAfterWorldEntry()
    {
        // During the load nothing may reach the player: state resolves, packets wait. The members used
        // here carry no reward, so the load window stays packet-clean by construction.
        _character.WorldEntryCompleted = false;
        CollectionsManager.Instance.Discover(_character, EquipItem, CollectionDiscoverySource.Equipped);
        CollectionsManager.Instance.Discover(_character, UnpackItem, CollectionDiscoverySource.Unpacked);
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
        await Assert.That(_character.Collections.InitialSyncPending).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(101u)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(102u)).IsTrue();

        // The replay carries exactly the collection rows of the achievement list.
        var rows = CollectionsManager.Instance.BuildInitialSyncRows(_character);
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].Id).IsEqualTo(101u);
        await Assert.That(rows[1].Id).IsEqualTo(102u);

        await Assert.That(CollectionsManager.Instance.FlushInitialSync(_character)).IsEqualTo(1);
        var opcodes = SentOpcodes().ToList();
        await Assert.That(opcodes.Count).IsEqualTo(1);
        await Assert.That(opcodes[0]).IsEqualTo(SCOffsets.SCAchievementsPacket);
        await Assert.That(_character.Collections.InitialSyncPending).IsFalse();

        // A second flush — a second NotifyInGameCompleted, for instance — sends nothing.
        _sentPackets.Clear();
        await Assert.That(CollectionsManager.Instance.FlushInitialSync(_character)).IsEqualTo(0);
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InitialSync_WithNothingTouchedSendsNoPacketsAndClearsTheQueue()
    {
        await Assert.That(CollectionsManager.Instance.FlushInitialSync(_character)).IsEqualTo(0);
        await Assert.That(_character.Collections.InitialSyncPending).IsFalse();
        await Assert.That(CollectionsManager.Instance.FlushInitialSync(_character)).IsEqualTo(0);
    }

    [Test]
    public async Task FlushInitialSync_WithoutCollectionStateSendsNothing()
    {
        _character.Collections = null;
        await Assert.That(CollectionsManager.Instance.FlushInitialSync(_character)).IsEqualTo(0);
    }

    // ---------------------------------------------------------------- restore and persistence

    [Test]
    public async Task Restore_UsesTheInjectedResolverAndNeverTouchesContentSingletons()
    {
        var collections = new CharacterCollections(_character);

        var kept = collections.Restore(
            new uint[] { CollectItem, NotAnEntryItem, CollectItem },
            entryId => entryId == CollectItem);

        await Assert.That(kept).IsEqualTo(1);
        await Assert.That(collections.IsDiscovered(CollectItem)).IsTrue();
        await Assert.That(collections.IsDiscovered(NotAnEntryItem)).IsFalse();
        // A restored set is still awaiting its first post-entry sync.
        await Assert.That(collections.InitialSyncPending).IsTrue();
    }

    [Test]
    public async Task Restore_WithoutAResolverKeepsEveryRow()
    {
        var collections = new CharacterCollections(_character);
        await Assert.That(collections.Restore(new uint[] { CollectItem, NotAnEntryItem }, null)).IsEqualTo(2);
        await Assert.That(collections.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Save_WhenTheConnectionCannotRunAStatement_TheFailureReachesTheCaller()
    {
        // A broken connection is not the feature table being missing: MySqlException (the missing-table
        // case the feature SQL degrades on) is caught, anything else must reach the save so it can roll
        // back rather than report success without the rows.
        var collections = new CharacterCollections(_character);
        collections.TryDiscover(CollectItem);

        var failure = Capture(() => collections.Save(new MySqlConnection(), null));

        await Assert.That(failure).IsNotNull();
        await Assert.That(collections.IsDiscovered(CollectItem)).IsTrue();
        // The in-memory ledger survived; nothing was silently dropped.
        await Assert.That(collections.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Load_WhenTheConnectionCannotRunAStatement_FailsWithoutTouchingState()
    {
        var collections = new CharacterCollections(_character);
        collections.TryDiscover(CollectItem);

        var failure = Capture(() => collections.Load(new MySqlConnection(), entryId => entryId == CollectItem));

        await Assert.That(failure).IsNotNull();
        await Assert.That(collections.Count).IsEqualTo(1);
        await Assert.That(collections.InitialSyncPending).IsTrue();
    }

    // ---------------------------------------------------------------- fixture helpers

    private IEnumerable<ushort> SentOpcodes() =>
        _sentPackets.Select(bytes => BitConverter.ToUInt16(bytes, 6));

    private static Exception Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void SeedContent()
    {
        Execute("INSERT INTO enum_achievement_kinds (id, name) VALUES (1, 'racial_mission')");
        Execute("INSERT INTO enum_achievement_kinds (id, name) VALUES (2, 'achievement')");
        Execute("INSERT INTO enum_achievement_kinds (id, name) VALUES (3, 'collection')");

        // Category 1 is an ordinary achievement category; category 9 carries the collection kind.
        Execute("INSERT INTO achievement_categories (id, achievement_kind_id) VALUES (1, 2)");
        Execute("INSERT INTO achievement_categories (id, achievement_kind_id) VALUES (9, 3)");
        // A category whose kind row is missing: content noise that must not be read as a collection.
        Execute("INSERT INTO achievement_categories (id, achievement_kind_id) VALUES (10, 99)");

        Execute($"INSERT INTO achievement_sub_categories (id, achievement_category_id) VALUES ({GeneralSubCategory}, 1)");
        Execute($"INSERT INTO achievement_sub_categories (id, achievement_category_id) VALUES ({CollectionSubCategory}, 9)");

        // Achievement 50: general category, watches the same item type the collection achievement does.
        InsertAchievement(50, 1, "t", "Pick the item up", subCategoryId: GeneralSubCategory);
        InsertObjective(50, 50, GeneralGetRecord);

        // Achievement 100: collection member that pays a title on completion.
        InsertAchievement(100, 1, "t", "Collect the item", subCategoryId: CollectionSubCategory,
            appellationId: CollectionRewardTitle);
        InsertObjective(100, 100, CollectGetRecord);

        // Achievement 101: collection member watching the equip event.
        InsertAchievement(101, 1, "t", "Equip the item", subCategoryId: CollectionSubCategory);
        InsertObjective(101, 101, CollectEquipRecord);

        // Achievement 102: collection member watching the unpack event.
        InsertAchievement(102, 1, "t", "Unpack the item", subCategoryId: CollectionSubCategory);
        InsertObjective(102, 102, CollectUnpackRecord);

        // A collection achievement pointing at a sub-category that does not exist: dropped loudly.
        InsertAchievement(103, 1, "t", "Dangling member", subCategoryId: 404);
        InsertObjective(103, 103, CollectGetRecord);

        // Watch records: kind ids follow the shipped record kinds for obtain / equip / unpack.
        InsertRecord(CollectGetRecord, 29, CollectItem, -1);
        InsertRecord(CollectEquipRecord, 83, EquipItem, -1);
        InsertRecord(CollectUnpackRecord, 82, UnpackItem, -1);
        InsertRecord(RecordOnlyItemRecord, 29, RecordOnlyItem, -1);
        InsertRecord(GeneralGetRecord, 29, CollectItem, -1);
        // A watch row without an item target: skipped, never discovered into.
        InsertRecord(2006, 29, 0, -1);

        foreach (var itemId in new[]
                 {
                     CollectItem, EquipItem, UnpackItem, RecordOnlyItem, EncyclopediaMember, DroppedMemberItem,
                 })
            Execute($"INSERT INTO items (id) VALUES ({itemId})");

        Execute("INSERT INTO item_guide_impls (id) VALUES (1)");
        Execute("INSERT INTO item_guide_a_categories (id, item_guide_impl_id) VALUES (600, 1)");
        Execute("INSERT INTO item_guide_b_categories (id, item_guide_a_category_id) VALUES (610, 600)");

        // One valid guide, one duplicate id row that also carries an unknown impl (must not replace the
        // first), and two guides whose impl does not exist.
        Execute($"INSERT INTO item_guides (id, item_guide_impl_id) VALUES ({EncyclopediaGuide}, 1)");
        Execute($"INSERT INTO item_guides (id, item_guide_impl_id) VALUES ({EncyclopediaGuide}, 99999)");
        Execute("INSERT INTO item_guides (id, item_guide_impl_id) VALUES (501, 2)");
        Execute("INSERT INTO item_guides (id, item_guide_impl_id) VALUES (502, 99999)");

        InsertElem(EncyclopediaMember, EncyclopediaGuide, 600, 610); // valid
        InsertElem(CollectItem, EncyclopediaGuide, 600, 610);        // valid
        InsertElem(DanglingMemberItem, EncyclopediaGuide, 600, 610); // unknown item -> skipped
        InsertElem(DroppedMemberItem, 404, 600, 610);                // unknown guide -> skipped
        InsertElem(EncyclopediaMember, 502, 600, 610);               // guide was skipped -> skipped
        InsertElem(EquipItem, EncyclopediaGuide, 777, 610);          // unknown category -> skipped
        InsertElem(UnpackItem, EncyclopediaGuide, 600, 888);         // unknown subcategory -> skipped
    }

    private void InsertAchievement(uint id, int completeNum, string completeOr, string name,
        uint subCategoryId = 1, uint appellationId = 0) =>
        Execute($"INSERT INTO achievements (id, name, summary, description, achievement_sub_category_id, " +
                $"parent_achievement_id, season_off, is_hidden, priority, or_unit_reqs, complete_or, " +
                $"complete_num, item_id, icon_id, item_num, grade_id, appellation_id, milestone_id) " +
                $"VALUES ({id}, '{name}', 'a summary', '', {subCategoryId}, 0, 'f', 'f', 0, 'f', " +
                $"'{completeOr}', {completeNum}, 0, 0, 0, 0, {appellationId}, 0)");

    private void InsertObjective(uint achievementId, uint objectiveId, uint recordId) =>
        Execute($"INSERT INTO achievement_objectives (id, achievement_id, or_unit_reqs, record_id) " +
                $"VALUES ({objectiveId}, {achievementId}, 'f', {recordId})");

    private void InsertRecord(uint id, long kind, long value1, long value2) =>
        Execute($"INSERT INTO char_records (id, kind_id, value1, value2) VALUES ({id}, {kind}, {value1}, {value2})");

    private void InsertElem(uint itemId, uint guideId, uint aCategoryId, uint? bCategoryId) =>
        Execute($"INSERT INTO item_guide_elems (item_id, item_guide_id, item_guide_a_category_id, " +
                $"item_guide_b_category_id) VALUES ({itemId}, {guideId}, {aCategoryId}, " +
                $"{(bCategoryId?.ToString() ?? "NULL")})");

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ResetSingleton<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
}
