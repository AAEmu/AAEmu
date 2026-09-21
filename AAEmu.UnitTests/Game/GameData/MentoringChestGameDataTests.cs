using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

public class MentoringChestGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("""
            CREATE TABLE quest_contexts (id INTEGER PRIMARY KEY, zone_id INTEGER);
            CREATE TABLE zones (id INTEGER PRIMARY KEY, group_id INTEGER);
            CREATE TABLE quest_components (id INTEGER PRIMARY KEY, quest_context_id INTEGER);
            CREATE TABLE quest_acts (
                id INTEGER PRIMARY KEY, quest_component_id INTEGER,
                act_detail_id INTEGER, act_detail_type TEXT);
            CREATE TABLE quest_act_obj_item_gathers (
                id INTEGER PRIMARY KEY, highlight_doodad_id INTEGER);
            CREATE TABLE doodad_func_groups (
                id INTEGER PRIMARY KEY, doodad_almighty_id INTEGER,
                doodad_func_group_kind_id INTEGER);
            CREATE TABLE doodad_funcs (
                id INTEGER PRIMARY KEY, doodad_func_group_id INTEGER,
                actual_func_id INTEGER, actual_func_type TEXT, next_phase INTEGER);
            CREATE TABLE doodad_func_skill_hits (id INTEGER PRIMARY KEY, skill_id INTEGER);
            CREATE TABLE indun_events (
                id INTEGER PRIMARY KEY, zone_group_id INTEGER, condition_id INTEGER,
                condition_type TEXT, start_action_id INTEGER);
            CREATE TABLE indun_event_npc_killeds (id INTEGER PRIMARY KEY, npc_id INTEGER);
            CREATE TABLE indun_actions (
                id INTEGER PRIMARY KEY, detail_id INTEGER, detail_type TEXT,
                next_action_id INTEGER);
            CREATE TABLE indun_action_change_doodad_phases (
                id INTEGER PRIMARY KEY, doodad_almighty_id INTEGER,
                doodad_func_group_id INTEGER);
            """);
    }

    [Test]
    public async Task Load_DerivesAllBossVariantsFromQuestActionsAndDeathSkills()
    {
        SeedCompleteContent();
        var data = new MentoringChestGameData();

        data.Load(Connection);

        await AssertTrigger(data, 50, 11364, 7522, 20831, 21043);
        await AssertTrigger(data, 50, 12188, 7522, 20831, 21043);
        await AssertTrigger(data, 45, 8962, 7659, 20853, 21051);
        await AssertTrigger(data, 47, 9797, 7660, 20846, 21050);
        await AssertTrigger(data, 47, 12189, 7660, 20846, 21050);
        await AssertTrigger(data, 46, 9798, 7661, 20848, 21056);
    }

    [Test]
    public async Task Load_DropsAnAmbiguousBossMapping()
    {
        SeedCompleteContent();
        Execute("""
            UPDATE indun_actions SET next_action_id = 900 WHERE id = 188;
            INSERT INTO indun_actions VALUES
                (900, 900, 'IndunActionChangeDoodadPhase', 0);
            INSERT INTO indun_action_change_doodad_phases VALUES
                (900, 7522, 29999);
            """);
        var data = new MentoringChestGameData();

        data.Load(Connection);

        await Assert.That(data.TryGetTrigger(50, 11364, out _)).IsFalse();
        await Assert.That(data.TryGetTrigger(50, 12188, out _)).IsFalse();
        await Assert.That(data.TryGetTrigger(45, 8962, out _)).IsTrue();
    }

    [Test]
    public async Task Load_TerminatesAnUnrelatedCyclicActionChain()
    {
        SeedCompleteContent();
        Execute("""
            INSERT INTO indun_event_npc_killeds VALUES (900, 55555);
            INSERT INTO indun_events VALUES
                (900, 50, 900, 'IndunEventNpcKilled', 901);
            INSERT INTO indun_actions VALUES
                (901, 0, 'NpcSpawnerSpawnEffect', 902),
                (902, 0, 'NpcSpawnerSpawnEffect', 901);
            """);
        var data = new MentoringChestGameData();

        data.Load(Connection);

        await Assert.That(data.TryGetTrigger(50, 55555, out _)).IsFalse();
        await Assert.That(data.TryGetTrigger(50, 11364, out _)).IsTrue();
    }

    private void SeedCompleteContent()
    {
        Execute("""
            INSERT INTO zones VALUES (184, 50), (168, 45), (170, 47), (169, 46);
            INSERT INTO doodad_func_groups VALUES
                (20831, 7522, 1), (20853, 7659, 1),
                (20846, 7660, 1), (20848, 7661, 1);
            """);

        SeedQuest(6083, 184, 7522, 1);
        SeedQuest(6084, 184, 7522, 2);
        SeedQuest(6087, 168, 7659, 3);
        SeedQuest(6088, 168, 7659, 4);
        SeedQuest(6166, 170, 7660, 5);
        SeedQuest(6167, 170, 7660, 6);
        SeedQuest(6168, 169, 7661, 7);
        SeedQuest(6169, 169, 7661, 8);

        Execute("""
            INSERT INTO indun_event_npc_killeds VALUES
                (1, 11364), (2, 12188), (3, 9797), (4, 12189);
            INSERT INTO indun_events VALUES
                (1, 50, 1, 'IndunEventNpcKilled', 186),
                (2, 50, 2, 'IndunEventNpcKilled', 186),
                (3, 47, 3, 'IndunEventNpcKilled', 184),
                (4, 47, 4, 'IndunEventNpcKilled', 184);
            INSERT INTO indun_actions VALUES
                (186, 131, 'IndunActionChangeDoodadPhase', 188),
                (188, 133, 'IndunActionChangeDoodadPhase', 0),
                (184, 5054, 'NpcSpawnerSpawnEffect', 189),
                (189, 134, 'IndunActionChangeDoodadPhase', 0);
            INSERT INTO indun_action_change_doodad_phases VALUES
                (131, 5724, 14674), (133, 7522, 21043), (134, 7660, 21050);

            INSERT INTO np_skills
                (id, owner_id, owner_type, skill_id, skill_use_condition_id)
            VALUES
                (101, 8962, 'Npc', 18024, 2),
                (102, 9798, 'Npc', 18031, 2),
                (103, 9797, 'Npc', 17118, 2),
                (104, 12189, 'Npc', 17118, 2);
            INSERT INTO doodad_func_skill_hits VALUES
                (1241, 18024), (1262, 18031), (1234, 17118);
            INSERT INTO doodad_funcs VALUES
                (1, 20853, 1241, 'DoodadFuncSkillHit', 21051),
                (2, 20848, 1262, 'DoodadFuncSkillHit', 21056),
                (3, 20846, 1234, 'DoodadFuncSkillHit', 21050);
            """);
    }

    private void SeedQuest(uint questId, uint zoneId, uint chestId, uint row)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quest_contexts VALUES (@quest, @zone);
            INSERT INTO quest_components VALUES (@component, @quest);
            INSERT INTO quest_acts VALUES (@act, @component, @detail, 'QuestActObjItemGather');
            INSERT INTO quest_act_obj_item_gathers VALUES (@detail, @chest);
            """;
        command.Parameters.AddWithValue("@quest", questId);
        command.Parameters.AddWithValue("@zone", zoneId);
        command.Parameters.AddWithValue("@component", 1000 + row);
        command.Parameters.AddWithValue("@act", 2000 + row);
        command.Parameters.AddWithValue("@detail", 3000 + row);
        command.Parameters.AddWithValue("@chest", chestId);
        command.ExecuteNonQuery();
    }

    private static async Task AssertTrigger(
        MentoringChestGameData data,
        uint zoneGroupId,
        uint npcId,
        uint chestId,
        uint initialPhaseId,
        uint exposedPhaseId)
    {
        var found = data.TryGetTrigger(zoneGroupId, npcId, out var trigger);
        await Assert.That(found).IsTrue();
        await Assert.That(trigger).IsEqualTo(
            new MentoringChestTrigger(zoneGroupId, npcId, chestId, initialPhaseId, exposedPhaseId));
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
