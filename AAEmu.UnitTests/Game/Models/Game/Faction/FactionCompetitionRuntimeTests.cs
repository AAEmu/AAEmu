using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.UnitTests.Game.Models.Game.Faction;

/// <summary>
/// A faction-competition runtime over a fixture with one competition per shipped reset state, so a
/// test can tell the three reset behaviours apart: competition 10 resets every faction and still
/// needs the required points, 11 resets only the winner, 12 resets every faction and ignores the
/// required points.
/// </summary>
[NotInParallel]
public sealed class FactionCompetitionRuntimeTests : SqliteTestBase
{
    private const uint FactionA = 148;
    private const uint FactionB = 149;
    private const uint ListedNpc = 100;
    private const uint UnlistedNpc = 101;
    private const uint ListedQuest = 200;
    private const uint UnlistedQuest = 201;

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

            INSERT INTO npcs VALUES (100), (101);
            INSERT INTO buffs VALUES (300);
            INSERT INTO quest_contexts VALUES (200), (201);
            INSERT INTO enum_faction_competition_reset_state_kinds VALUES
                (1, 'all'), (2, 'winnerOnly'), (3, 'all_ignoreReqPoint');
            INSERT INTO faction_competitions VALUES
                (10, 'reset all',        1, 5,  50,  20, 1, 'f', NULL, '', 1, 'CompetitionPvp'),
                (11, 'reset winner only',1, 5,  50,  20, 2, 'f', NULL, '', 2, 'CompetitionPvp'),
                (12, 'reset all, no req', 1, 5,  50,  20, 3, 'f', NULL, '', 3, 'CompetitionPvp');
            -- The listed NPC is linked to all three competitions, and is linked to 10 twice so the
            -- de-duplication is observable.
            INSERT INTO faction_competition_npc_infos VALUES
                (1, 10, 100), (2, 10, 100), (3, 11, 100), (4, 12, 100);
            INSERT INTO faction_competition_quest_infos VALUES (1, 11, 200);
            INSERT INTO zone_score_contents VALUES (1, 'score', 't', 5, 300, 0);
            INSERT INTO zone_score_kinds VALUES
                (1, 1, 1, 't', 'Score', 0, 100, 't', 't', 'Level', 't', 'f', 'f', 'f', 'f');
            INSERT INTO zone_score_levels VALUES (1, 1, 0, 0, 0);
            INSERT INTO zone_score_kind_rank_details VALUES (1, 1);
            """;
        command.ExecuteNonQuery();
    }

    private FactionCompetitionRuntime NewRuntime(IFactionCompetitionRuntimeStore store = null)
    {
        FactionScoringGameData.Instance.Load(Connection);
        return new FactionCompetitionRuntime(FactionScoringGameData.Instance, store);
    }

    /// <summary>Scores one faction <paramref name="kills"/> times, five points each.</summary>
    private static void Score(FactionCompetitionRuntime runtime, uint factionId, uint npcId, int kills)
    {
        for (var i = 0; i < kills; i++)
            runtime.RegisterNpcKill(factionId, npcId);
    }

    [Test]
    public async Task ListedNpcKillScoresEveryLinkedCompetitionExactlyOnce()
    {
        var runtime = NewRuntime();
        runtime.Load();

        var applications = runtime.RegisterNpcKill(FactionA, ListedNpc);

        // Three competitions, despite competition 10 carrying the link twice.
        await Assert.That(applications.Select(a => a.CompetitionId)).IsEquivalentTo(new uint[] { 10, 11, 12 });
        await Assert.That(runtime.GetScore(10, FactionA)).IsEqualTo(5L);
        await Assert.That(runtime.GetScore(11, FactionA)).IsEqualTo(5L);
        await Assert.That(runtime.GetScore(12, FactionA)).IsEqualTo(5L);
    }

    [Test]
    public async Task UnlistedNpcScoresNothingAtAll()
    {
        var runtime = NewRuntime();
        runtime.Load();
        var published = 0;
        runtime.ScoreChanged += _ => published++;

        await Assert.That(runtime.RegisterNpcKill(FactionA, UnlistedNpc)).IsEmpty();
        await Assert.That(published).IsEqualTo(0);
        await Assert.That(runtime.GetScores(10)).IsEmpty();
    }

    [Test]
    public async Task QuestCompletionUsesTheShippedPointValueOfEachLinkedCompetition()
    {
        var runtime = NewRuntime();
        runtime.Load();

        var applications = runtime.RegisterQuestComplete(FactionA, ListedQuest);

        await Assert.That(applications.Count).IsEqualTo(1);
        await Assert.That(applications[0].CompetitionId).IsEqualTo(11u);
        await Assert.That(applications[0].AppliedDelta).IsEqualTo(50);
        await Assert.That(runtime.GetScore(11, FactionA)).IsEqualTo(50L);
    }

    [Test]
    public async Task UnlinkedQuestContextScoresNothing()
    {
        var runtime = NewRuntime();
        runtime.Load();

        await Assert.That(runtime.RegisterQuestComplete(FactionA, UnlistedQuest)).IsEmpty();
    }

    [Test]
    public async Task AnEligibleEventThatScoresZeroIsStillPublished()
    {
        Execute("UPDATE faction_competitions SET point_npc_kill_value = 0 WHERE id = 10");
        var runtime = NewRuntime();
        runtime.Load();
        var published = 0;
        runtime.ScoreChanged += _ => published++;

        var applications = runtime.RegisterNpcKill(FactionA, ListedNpc);

        // Competition 10 credited nothing; that is different from being ineligible, and the change
        // is still published so a caller can see the event was considered.
        await Assert.That(applications.Count).IsEqualTo(3);
        await Assert.That(applications.Single(a => a.CompetitionId == 10).AppliedDelta).IsEqualTo(0);
        await Assert.That(published).IsEqualTo(3);
    }

    [Test]
    public async Task WinnerOnlyResetClearsTheWinnerAndLeavesTheLoser()
    {
        var runtime = NewRuntime();
        runtime.Load();
        // Five kills is 25 points, past this competition's required 20; one kill is 5, under it.
        Score(runtime, FactionA, ListedNpc, 5);
        Score(runtime, FactionB, ListedNpc, 1);

        var resolution = runtime.ResolveAndReset(11);

        await Assert.That(resolution.WinnerFactionId).IsEqualTo(FactionA);
        await Assert.That(resolution.ResetState).IsEqualTo(FactionCompetitionResetStateKind.WinnerOnly);
        await Assert.That(resolution.ResetFactionIds).IsEquivalentTo(new uint[] { FactionA });
        await Assert.That(runtime.GetScore(11, FactionA)).IsEqualTo(0L);
        await Assert.That(runtime.GetScore(11, FactionB)).IsEqualTo(5L);
    }

    [Test]
    public async Task ResetAllClearsEveryFactionIncludingTheLoser()
    {
        var runtime = NewRuntime();
        runtime.Load();
        Score(runtime, FactionA, ListedNpc, 5);
        Score(runtime, FactionB, ListedNpc, 1);

        var resolution = runtime.ResolveAndReset(10);

        await Assert.That(resolution.WinnerFactionId).IsEqualTo(FactionA);
        await Assert.That(resolution.ResetState).IsEqualTo(FactionCompetitionResetStateKind.All);
        await Assert.That(resolution.ResetFactionIds).IsEquivalentTo(new uint[] { FactionA, FactionB });
        await Assert.That(runtime.GetScores(10)).IsEmpty();
    }

    [Test]
    public async Task ARequiredPointsGateKeepsAHighScorerFromWinning()
    {
        // Both factions are under req_point 20; neither wins and the reset still clears both,
        // because the reset state is independent of whether there was a winner.
        var runtime = NewRuntime();
        runtime.Load();
        runtime.RegisterNpcKill(FactionA, ListedNpc);
        runtime.RegisterNpcKill(FactionB, ListedNpc);
        runtime.RegisterNpcKill(FactionB, ListedNpc);

        var resolution = runtime.ResolveAndReset(10);

        await Assert.That(resolution.WinnerFactionId).IsNull();
        await Assert.That(resolution.ResetFactionIds).IsEquivalentTo(new uint[] { FactionA, FactionB });
    }

    [Test]
    public async Task TheIgnoreRequiredPointsStateLetsScoreAloneDecide()
    {
        // The same scores that produced no winner under competition 10 produce a winner under 12,
        // which is exactly what the shipped reset-state name says.
        var runtime = NewRuntime();
        runtime.Load();
        runtime.RegisterNpcKill(FactionA, ListedNpc);
        runtime.RegisterNpcKill(FactionB, ListedNpc);
        runtime.RegisterNpcKill(FactionB, ListedNpc);

        var gated = runtime.ResolveAndReset(10);
        var ignored = runtime.ResolveAndReset(12);

        await Assert.That(gated.WinnerFactionId).IsNull();
        await Assert.That(ignored.WinnerFactionId).IsEqualTo(FactionB);
        await Assert.That(ignored.ResetState).IsEqualTo(FactionCompetitionResetStateKind.AllIgnoreRequiredPoints);
    }

    [Test]
    public async Task ATieIsLeftUnresolvedAndWinnerOnlyThenResetsNobody()
    {
        var runtime = NewRuntime();
        runtime.Load();
        // Both clear the required 20 and finish on the same score, so this is a real tie and not
        // the required-points gate.
        Score(runtime, FactionA, ListedNpc, 5);
        Score(runtime, FactionB, ListedNpc, 5);

        var resolution = runtime.ResolveAndReset(11);

        // No shipped column names a tie-break, so neither faction is declared the winner.
        await Assert.That(resolution.WinnerFactionId).IsNull();
        await Assert.That(resolution.ResetFactionIds).IsEmpty();
        await Assert.That(runtime.GetScore(11, FactionA)).IsEqualTo(25L);
        await Assert.That(runtime.GetScore(11, FactionB)).IsEqualTo(25L);
    }

    [Test]
    public async Task AnUnknownResetStateNameIsRefusedRatherThanGuessed()
    {
        Execute("UPDATE enum_faction_competition_reset_state_kinds SET name = 'somethingNew' WHERE id = 1");
        var runtime = NewRuntime();
        runtime.Load();
        runtime.RegisterNpcKill(FactionA, ListedNpc);

        await Assert.That(() => runtime.ResolveAndReset(10)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ScoresArePersistedAndRestoredAcrossARestart()
    {
        var store = new RecordingCompetitionStore();
        var first = NewRuntime(store);
        first.Load();
        first.RegisterNpcKill(FactionA, ListedNpc);

        // A fresh runtime over the same store is the restart case: the score comes back.
        var second = NewRuntime(store);
        second.Load();

        await Assert.That(second.GetScore(10, FactionA)).IsEqualTo(5L);
        await Assert.That(second.GetScore(11, FactionA)).IsEqualTo(5L);
    }

    [Test]
    public async Task AResetDeletesTheStoreRowsSoARestartDoesNotResurrectThem()
    {
        var store = new RecordingCompetitionStore();
        var runtime = NewRuntime(store);
        runtime.Load();
        runtime.RegisterNpcKill(FactionA, ListedNpc);
        runtime.RegisterNpcKill(FactionB, ListedNpc);

        runtime.ResolveAndReset(10);

        var restarted = NewRuntime(store);
        restarted.Load();
        await Assert.That(restarted.GetScores(10)).IsEmpty();
    }

    [Test]
    public async Task AStoredRowForAnUnknownCompetitionFailsLoudlyOnLoad()
    {
        var store = new RecordingCompetitionStore();
        store.Seed((999, FactionA), 40);

        var runtime = NewRuntime(store);

        await Assert.That(() => runtime.Load()).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task AZeroFactionCannotScore()
    {
        var runtime = NewRuntime();
        runtime.Load();

        await Assert.That(() => runtime.ApplyOne(0, 10, FactionCompetitionEventKind.NpcKill, ListedNpc))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AnUnknownCompetitionFailsLoudly()
    {
        var runtime = NewRuntime();
        runtime.Load();

        await Assert.That(() => runtime.ApplyOne(FactionA, 999, FactionCompetitionEventKind.NpcKill, ListedNpc))
            .Throws<KeyNotFoundException>();
        await Assert.That(() => runtime.GetScore(999, FactionA)).Throws<KeyNotFoundException>();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class RecordingCompetitionStore : IFactionCompetitionRuntimeStore
    {
        private readonly Dictionary<(uint, uint), long> _rows = [];

        public void Seed((uint CompetitionId, uint FactionId) key, long score) => _rows[key] = score;

        public IReadOnlyDictionary<(uint, uint), long> LoadAll() => _rows;

        public void Save(uint competitionId, uint factionId, long score) => _rows[(competitionId, factionId)] = score;

        public void Delete(uint competitionId, uint factionId) => _rows.Remove((competitionId, factionId));
    }
}
