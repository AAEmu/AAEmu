using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

/// <summary>
/// A zone-score runtime over a fixture whose cap and level ladder are chosen so a capped delta and
/// an unscored delta are different numbers: the cap sits between the level-one and level-two
/// thresholds, so hitting the cap has already earned level one and crediting zero has earned none.
/// </summary>
[NotInParallel]
public sealed class ZoneScoreRuntimeTests : SqliteTestBase
{
    private const uint ZoneGroupId = 5;

    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE npcs (id INTEGER PRIMARY KEY);
            CREATE TABLE buffs (id INTEGER PRIMARY KEY);
            CREATE TABLE quest_contexts (id INTEGER PRIMARY KEY);
            CREATE TABLE enum_faction_competition_reset_state_kinds (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE faction_competitions (
                id INTEGER PRIMARY KEY, comments TEXT NOT NULL, point_pc_kill_value INTEGER NOT NULL,
                point_npc_kill_value INTEGER NOT NULL, point_quest_complete_value INTEGER NOT NULL,
                req_point INTEGER NOT NULL, point_reset_id INTEGER NOT NULL, force_change_state TEXT NOT NULL,
                force_stop_tower_def_id INTEGER NULL, tooltip TEXT NOT NULL, detail_id INTEGER NOT NULL,
                detail_type TEXT NOT NULL
            );
            CREATE TABLE faction_competition_npc_infos (
                id INTEGER PRIMARY KEY, faction_competition_id INTEGER NOT NULL, npc_id INTEGER NOT NULL
            );
            CREATE TABLE faction_competition_quest_infos (
                id INTEGER PRIMARY KEY, faction_competition_id INTEGER NOT NULL, context_id INTEGER NOT NULL
            );
            CREATE TABLE zone_score_contents (
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, show_hud TEXT NOT NULL, zone_group_id INTEGER NOT NULL,
                buff_id INTEGER NOT NULL, quest_id INTEGER NOT NULL
            );
            CREATE TABLE zone_score_kinds (
                id INTEGER PRIMARY KEY, content_id INTEGER NOT NULL, ui_order INTEGER NOT NULL, db_save TEXT NOT NULL,
                score_name TEXT NOT NULL, icon_id INTEGER NOT NULL, max_score INTEGER NOT NULL, show_score TEXT NOT NULL,
                show_max_score TEXT NOT NULL, level_name TEXT NOT NULL, show_max_level TEXT NOT NULL,
                reset_zone_in TEXT NOT NULL, reset_zone_out TEXT NOT NULL, reset_buff_destroyed TEXT NOT NULL,
                reset_quest_removed TEXT NOT NULL
            );
            CREATE TABLE zone_score_levels (
                id INTEGER PRIMARY KEY, kind_id INTEGER NOT NULL, level INTEGER NOT NULL, req_score INTEGER NOT NULL,
                buff_id INTEGER NOT NULL
            );
            CREATE TABLE zone_score_kind_rank_details (id INTEGER PRIMARY KEY, zone_score_kind_id INTEGER NOT NULL);

            INSERT INTO npcs VALUES (100);
            INSERT INTO buffs VALUES (300);
            INSERT INTO quest_contexts VALUES (200);
            INSERT INTO enum_faction_competition_reset_state_kinds VALUES (1, 'all');
            INSERT INTO faction_competitions VALUES (10, 'sample', 1, 2, 3, 100, 1, 'f', NULL, '', 1, 'CompetitionPvp');
            INSERT INTO faction_competition_npc_infos VALUES (1, 10, 100);
            INSERT INTO faction_competition_quest_infos VALUES (1, 10, 200);

            -- Two contents on two zone groups: 1 owns kind 1 (saved, resets on zone in/out),
            -- 2 owns kind 2 (transient) and 3 (saved, never resets).
            INSERT INTO zone_score_contents VALUES
                (1, 'saved', 't', 5, 300, 0),
                (2, 'transient', 't', 5, 0, 0),
                (3, 'other-group', 'f', 9, 0, 0);
            INSERT INTO zone_score_kinds VALUES
                (1, 1, 1, 't', 'Saved', 0, 70, 't', 't', 'L', 't', 't', 't', 'f', 'f'),
                (2, 2, 2, 'f', 'Transient', 0, 70, 't', 't', 'L', 't', 'f', 'f', 'f', 'f'),
                (3, 3, 1, 't', 'Other', 0, 70, 't', 't', 'L', 't', 'f', 'f', 'f', 'f');
            -- Cap 70 sits above level 1 (50) and below level 2 (200): reaching the cap has earned
            -- level 1 only, so "capped" and "credited nothing" cannot produce the same state.
            INSERT INTO zone_score_levels VALUES
                (1, 1, 0, 0, 0), (2, 1, 1, 50, 0), (3, 1, 2, 200, 0),
                (4, 2, 0, 0, 0), (5, 2, 1, 50, 0), (6, 2, 2, 200, 0),
                (7, 3, 0, 0, 0), (8, 3, 1, 50, 0), (9, 3, 2, 200, 0);
            """;
        command.ExecuteNonQuery();
    }

    private ZoneScoreRuntime NewRuntime(IZoneScoreRuntimeStore store = null)
    {
        FactionScoringGameData.Instance.Load(Connection);
        return new ZoneScoreRuntime(ZoneGroupId, FactionScoringGameData.Instance, store);
    }

    [Test]
    public async Task FreshRuntimeStartsEveryOwnedKindAtItsLevelZeroBaseline()
    {
        var runtime = NewRuntime();
        runtime.Load();

        // Kind 3 belongs to zone group 9, so this runtime does not own it.
        await Assert.That(runtime.Snapshot().Select(entry => entry.KindId)).IsEquivalentTo(new uint[] { 1, 2 });
        await Assert.That(runtime.Get(1)).IsEqualTo(new ZoneScoreRuntimeEntry(1, ZoneGroupId, 0, 0));
    }

    [Test]
    public async Task DeltaRaisesScoreAndLevelFromTheShippedThresholds()
    {
        var runtime = NewRuntime();
        runtime.Load();

        var applied = runtime.Apply(1, 60);

        await Assert.That(applied.Score).IsEqualTo(60L);
        await Assert.That(applied.AppliedDelta).IsEqualTo(60);
        await Assert.That(applied.Level).IsEqualTo(1);
        await Assert.That(applied.PreviousLevel).IsEqualTo(0);
        await Assert.That(applied.LevelChanged).IsTrue();
        await Assert.That(applied.Clamped).IsFalse();
    }

    /// <summary>
    /// The cap test that matters: a request well past <c>max_score</c> must be reported as clamped
    /// with the reduced applied delta, and must be distinguishable from a request that credited
    /// nothing at all.
    /// </summary>
    [Test]
    public async Task DeltaPastMaxScoreIsClampedAndReportsHowMuchWasActuallyCredited()
    {
        var runtime = NewRuntime();
        runtime.Load();
        runtime.Apply(1, 60);

        var capped = runtime.Apply(1, 500);
        var refused = runtime.Apply(1, 0);

        await Assert.That(capped.RequestedDelta).IsEqualTo(500);
        await Assert.That(capped.AppliedDelta).IsEqualTo(10);
        await Assert.That(capped.Score).IsEqualTo(70L);
        await Assert.That(capped.Clamped).IsTrue();
        // Capped is not "did not score": the score still moved and the level still moved with it.
        await Assert.That(capped.AppliedDelta).IsNotEqualTo(0);
        await Assert.That(capped.Level).IsEqualTo(1);
        // A zero delta at the cap credits nothing and is not a clamp.
        await Assert.That(refused.AppliedDelta).IsEqualTo(0);
        await Assert.That(refused.Clamped).IsFalse();
    }

    [Test]
    public async Task LevelDoesNotAdvancePastTheCapEvenWhenTheNextThresholdIsBelowIt()
    {
        // The fixture's level 2 needs 200 but the cap is 70, so the cap - not the ladder - is what
        // stops the climb. A score at the cap must never resolve to level 2.
        var runtime = NewRuntime();
        runtime.Load();
        runtime.Apply(1, 1000);

        await Assert.That(runtime.Get(1).Score).IsEqualTo(70L);
        await Assert.That(runtime.Get(1).Level).IsEqualTo(1);
    }

    [Test]
    public async Task NegativeDeltaDropsScoreAndLevelBackDown()
    {
        var runtime = NewRuntime();
        runtime.Load();
        runtime.Apply(1, 60);

        var applied = runtime.Apply(1, -30);

        await Assert.That(applied.Score).IsEqualTo(30L);
        await Assert.That(applied.Level).IsEqualTo(0);
        await Assert.That(applied.LevelChanged).IsTrue();
    }

    [Test]
    public async Task ResetClearsScoreBackToTheLevelZeroBaseline()
    {
        var runtime = NewRuntime();
        runtime.Load();
        runtime.Apply(1, 60);
        var resets = new List<uint>();
        var changed = new List<ZoneScoreApplication>();
        runtime.ScoreReset += (kindId, _) => resets.Add(kindId);
        runtime.ScoreChanged += changed.Add;

        await Assert.That(runtime.Reset(1, ZoneScoreResetCause.ZoneIn)).IsTrue();

        await Assert.That(resets).IsEquivalentTo(new uint[] { 1 });
        await Assert.That(changed).HasCount(1);
        await Assert.That(runtime.Get(1).Score).IsEqualTo(0L);
        await Assert.That(runtime.Get(1).Level).IsEqualTo(0);
    }

    [Test]
    public async Task ResetCauseTheKindDoesNotOptIntoIsRefusedAndKeepsTheScore()
    {
        var runtime = NewRuntime();
        runtime.Load();
        runtime.Apply(1, 60);
        var published = 0;
        runtime.ScoreChanged += _ => published++;

        // Kind 1 sets reset_zone_in and reset_zone_out only.
        await Assert.That(runtime.Reset(1, ZoneScoreResetCause.BuffDestroyed)).IsFalse();
        await Assert.That(runtime.Reset(1, ZoneScoreResetCause.QuestRemoved)).IsFalse();

        await Assert.That(runtime.Get(1).Score).IsEqualTo(60L);
        await Assert.That(published).IsEqualTo(0);
    }

    [Test]
    public async Task AKindOwnedByAnotherZoneGroupIsRefused()
    {
        var runtime = NewRuntime();
        runtime.Load();

        await Assert.That(() => runtime.Apply(3, 10)).Throws<InvalidOperationException>();
        await Assert.That(() => runtime.Get(3)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnknownKindFailsLoudly()
    {
        var runtime = NewRuntime();
        runtime.Load();

        await Assert.That(() => runtime.Apply(999, 10)).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task SavedKindsAreWrittenAndTransientKindsAreNot()
    {
        var store = new RecordingZoneScoreStore();
        var runtime = NewRuntime(store);
        runtime.Load();

        runtime.Apply(1, 10);
        runtime.Apply(2, 10);

        await Assert.That(store.Saved.Select(entry => entry.KindId)).IsEquivalentTo(new uint[] { 1 });
    }

    [Test]
    public async Task StoredScoreIsRestoredAndItsLevelRederivedFromTheCatalog()
    {
        var store = new RecordingZoneScoreStore();
        store.Seed(new ZoneScoreRuntimeEntry(1, ZoneGroupId, 55, 99));

        var runtime = NewRuntime(store);
        runtime.Load();

        // Score survives; the hand-written level 99 is discarded in favour of the catalog level.
        await Assert.That(runtime.Get(1).Score).IsEqualTo(55L);
        await Assert.That(runtime.Get(1).Level).IsEqualTo(1);
    }

    [Test]
    public async Task StoredScoreAboveTheCapIsRestoredThroughTheCap()
    {
        var store = new RecordingZoneScoreStore();
        store.Seed(new ZoneScoreRuntimeEntry(1, ZoneGroupId, 5000, 2));

        var runtime = NewRuntime(store);
        runtime.Load();

        await Assert.That(runtime.Get(1).Score).IsEqualTo(70L);
        await Assert.That(runtime.Get(1).Level).IsEqualTo(1);
    }

    [Test]
    public async Task TransientKindRestartsFromZeroEvenWithAStoredRow()
    {
        var store = new RecordingZoneScoreStore();
        store.Seed(new ZoneScoreRuntimeEntry(2, ZoneGroupId, 60, 1));

        var runtime = NewRuntime(store);
        runtime.Load();

        // db_save is false for kind 2, so nothing is restored even if a row survived some other path.
        await Assert.That(runtime.Get(2).Score).IsEqualTo(0L);
        await Assert.That(runtime.Get(2).Level).IsEqualTo(0);
    }

    [Test]
    public async Task KindWithoutALevelZeroBaselineFailsLoudlyAtLoad()
    {
        Execute("DELETE FROM zone_score_levels WHERE kind_id = 1 AND level = 0");
        FactionScoringGameData.Instance.Load(Connection);
        var runtime = new ZoneScoreRuntime(ZoneGroupId, FactionScoringGameData.Instance);

        // Boot binds every owned kind, so a kind that cannot name its own start is refused before
        // any delta is accepted rather than at the first score.
        await Assert.That(() => runtime.Load()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ALadderThatStartsAboveZeroIsRefused()
    {
        Execute("UPDATE zone_score_levels SET req_score = 10 WHERE kind_id = 1 AND level = 0");
        FactionScoringGameData.Instance.Load(Connection);
        var runtime = new ZoneScoreRuntime(ZoneGroupId, FactionScoringGameData.Instance);

        // A level-zero row requiring a positive score would leave the kind unable to represent the
        // start of its own range, so the rules refuse the content instead of picking a floor.
        await Assert.That(() => runtime.Load()).Throws<InvalidOperationException>();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class RecordingZoneScoreStore : IZoneScoreRuntimeStore
    {
        private readonly Dictionary<uint, ZoneScoreRuntimeEntry> _rows = [];

        public List<ZoneScoreRuntimeEntry> Saved { get; } = [];

        public void Seed(ZoneScoreRuntimeEntry entry) => _rows[entry.KindId] = entry;

        public IReadOnlyDictionary<uint, ZoneScoreRuntimeEntry> Load(uint zoneGroupId) =>
            _rows.Values.Where(entry => entry.ZoneGroupId == zoneGroupId).ToDictionary(entry => entry.KindId);

        public void Save(ZoneScoreRuntimeEntry entry)
        {
            _rows[entry.KindId] = entry;
            Saved.Add(entry);
        }
    }
}
