using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

[NotInParallel]
public sealed class FactionScoringGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
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
            INSERT INTO quest_contexts VALUES (200);
            INSERT INTO enum_faction_competition_reset_state_kinds VALUES (1, 'all'), (2, 'winnerOnly');
            INSERT INTO faction_competitions VALUES
                (10, 'sample', 1, 2, 3, 100, 2, 'f', NULL, '', 1, 'CompetitionPvp');
            INSERT INTO faction_competition_npc_infos VALUES (1, 10, 100), (2, 10, 100), (3, 10, 101);
            INSERT INTO faction_competition_quest_infos VALUES (1, 10, 200);
            INSERT INTO zone_score_contents VALUES (1, 'score', 't', 5, 300, 0);
            INSERT INTO zone_score_kinds VALUES
                (1, 1, 1, 't', 'Score', 0, 100, 't', 't', 'Level', 't', 'f', 'f', 'f', 'f');
            INSERT INTO zone_score_levels VALUES (1, 1, 0, 0, 0), (2, 1, 1, 50, 300);
            INSERT INTO zone_score_kind_rank_details VALUES (1, 1);
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task LoadExposesTypedMetadataAndPreservesDuplicateLinkRows()
    {
        FactionScoringGameData.Instance.Load(Connection);

        var competition = FactionScoringGameData.Instance.GetCompetition(10);
        await Assert.That(competition.PlayerKillPoints).IsEqualTo(1);
        await Assert.That(competition.NpcKillPoints).IsEqualTo(2);
        await Assert.That(competition.QuestCompletePoints).IsEqualTo(3);
        await Assert.That(competition.RequiredPoints).IsEqualTo(100);
        await Assert.That(competition.ForceStopTowerDefId).IsNull();
        await Assert.That(competition.PointResetId).IsEqualTo(2u);
        await Assert.That(FactionScoringGameData.Instance.GetResetState(2).Name).IsEqualTo("winnerOnly");

        var npcLinks = FactionScoringGameData.Instance.GetNpcLinks(10);
        await Assert.That(npcLinks.Count).IsEqualTo(3);
        await Assert.That(npcLinks.Count(link => link.NpcId == 100)).IsEqualTo(2);
        await Assert.That(FactionScoringGameData.Instance.GetQuestLinks(10).Single().QuestContextId).IsEqualTo(200u);

        var kind = FactionScoringGameData.Instance.GetZoneScoreKind(1);
        await Assert.That(kind.ContentId).IsEqualTo(1u);
        await Assert.That(kind.MaxScore).IsEqualTo(100);
        await Assert.That(FactionScoringGameData.Instance.GetZoneScoreKinds(1).Count).IsEqualTo(1);

        var levels = FactionScoringGameData.Instance.GetZoneScoreLevels(1);
        await Assert.That(levels.Count).IsEqualTo(2);
        await Assert.That(levels[0].Level).IsEqualTo(0);
        await Assert.That(levels[1].Level).IsEqualTo(1);
        await Assert.That(levels[1].RequiredScore).IsEqualTo(50L);
        await Assert.That(FactionScoringGameData.Instance.GetZoneScoreRankDetails(1).Single().Id).IsEqualTo(1u);
    }

    [Test]
    public async Task ZeroTowerStopMetadataIsPreservedAsContent()
    {
        Execute("UPDATE faction_competitions SET force_stop_tower_def_id = 0 WHERE id = 10");

        FactionScoringGameData.Instance.Load(Connection);

        await Assert.That(FactionScoringGameData.Instance.GetCompetition(10).ForceStopTowerDefId)
            .IsEqualTo((uint?)0u);
    }

    [Test]
    public async Task MissingCompetitionLookupFailsLoudly()
    {
        FactionScoringGameData.Instance.Load(Connection);

        await Assert.That(() => FactionScoringGameData.Instance.GetCompetition(999))
            .Throws<KeyNotFoundException>();
        await Assert.That(() => FactionScoringGameData.Instance.GetZoneScoreKind(999))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task MissingResetStateFailsDuringLoad()
    {
        Execute("UPDATE faction_competitions SET point_reset_id = 99 WHERE id = 10");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task OrphanNpcLinkFailsDuringLoad()
    {
        Execute("UPDATE faction_competition_npc_infos SET npc_id = 999 WHERE id = 3");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task OrphanQuestLinkFailsDuringLoad()
    {
        Execute("UPDATE faction_competition_quest_infos SET context_id = 999 WHERE id = 1");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task OrphanZoneScoreParentFailsDuringLoad()
    {
        Execute("UPDATE zone_score_kinds SET content_id = 999 WHERE id = 1");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task OrphanZoneScoreLevelFailsDuringLoad()
    {
        Execute("UPDATE zone_score_levels SET kind_id = 999 WHERE id = 2");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task OrphanBuffFailsDuringLoad()
    {
        Execute("UPDATE zone_score_levels SET buff_id = 999 WHERE id = 2");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task DuplicateLevelNumberFailsDuringLoad()
    {
        Execute("INSERT INTO zone_score_levels VALUES (3, 1, 1, 10, 0)");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task KindWithoutLevelFailsDuringLoad()
    {
        Execute("DELETE FROM zone_score_levels WHERE kind_id = 1");

        await Assert.That(() => FactionScoringGameData.Instance.Load(Connection))
            .Throws<InvalidOperationException>();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
