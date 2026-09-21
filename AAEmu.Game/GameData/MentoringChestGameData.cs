using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Resolves the dungeon boss deaths which expose the retained mentoring quest chests.
/// The restored quest list selects the retired feature; boss, dungeon, chest, and phase values
/// remain owned by game content.
/// </summary>
[GameData]
public class MentoringChestGameData : Singleton<MentoringChestGameData>, IGameDataLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<(uint ZoneGroupId, uint NpcId), MentoringChestTrigger> _triggers = [];

    public void Load(SqliteConnection connection)
    {
        _triggers = [];

        var chestZones = LoadRestoredChestZones(connection);
        var candidates = new Dictionary<(uint ZoneGroupId, uint NpcId), HashSet<(uint ChestId, uint PhaseId)>>();

        LoadIndunActionCandidates(connection, chestZones, candidates);
        LoadOnDeathSkillCandidates(connection, chestZones, candidates);

        foreach (var (key, values) in candidates)
        {
            if (values.Count != 1)
            {
                Logger.Error(
                    "Mentoring chest mapping is ambiguous for zoneGroupId={0}, npcId={1}: {2}",
                    key.ZoneGroupId,
                    key.NpcId,
                    string.Join(", ", values.Select(x => $"{x.ChestId}/{x.PhaseId}")));
                continue;
            }

            var value = values.Single();
            var source = chestZones[value.ChestId];
            _triggers[key] = new MentoringChestTrigger(
                key.ZoneGroupId,
                key.NpcId,
                value.ChestId,
                source.InitialPhaseId,
                value.PhaseId);
        }

        foreach (var chest in chestZones.Keys)
        {
            if (_triggers.Values.All(x => x.DoodadTemplateId != chest))
                Logger.Error("No boss activation was found for restored mentoring chest doodad={0}", chest);
        }

        Logger.Info("Loaded {0} mentoring chest boss trigger(s) from game content", _triggers.Count);
    }

    public void PostLoad()
    {
    }

    public bool TryGetTrigger(uint zoneGroupId, uint npcId, out MentoringChestTrigger trigger) =>
        _triggers.TryGetValue((zoneGroupId, npcId), out trigger);

    private static Dictionary<uint, MentoringChestSource> LoadRestoredChestZones(SqliteConnection connection)
    {
        var questRows = new Dictionary<uint, HashSet<MentoringChestSource>>();
        var restoredQuestIds = MentoringQuestRestoration.QuestIds.ToHashSet();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT qc.id AS quest_id, z.group_id AS zone_group_id,
                   gather.highlight_doodad_id AS doodad_id,
                   initial_group.id AS initial_phase_id
              FROM quest_contexts qc
              JOIN zones z ON z.id = qc.zone_id
              JOIN quest_components component ON component.quest_context_id = qc.id
              JOIN quest_acts act ON act.quest_component_id = component.id
              JOIN quest_act_obj_item_gathers gather
                ON act.act_detail_type = 'QuestActObjItemGather'
               AND gather.id = act.act_detail_id
              JOIN doodad_func_groups initial_group
                ON initial_group.doodad_almighty_id = gather.highlight_doodad_id
               AND initial_group.doodad_func_group_kind_id = @startGroupKind
             WHERE gather.highlight_doodad_id > 0
            """;
        command.Parameters.AddWithValue("@startGroupKind", (uint)DoodadFuncGroups.DoodadFuncGroupKind.Start);
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var questId = reader.GetUInt32("quest_id");
            if (!restoredQuestIds.Contains(questId))
                continue;

            if (!questRows.TryGetValue(questId, out var rows))
            {
                rows = [];
                questRows[questId] = rows;
            }

            rows.Add(new MentoringChestSource(
                reader.GetUInt32("doodad_id"),
                reader.GetUInt32("zone_group_id"),
                reader.GetUInt32("initial_phase_id")));
        }

        var chestZones = new Dictionary<uint, MentoringChestSource>();
        var invalidChests = new HashSet<uint>();
        foreach (var questId in restoredQuestIds)
        {
            if (!questRows.TryGetValue(questId, out var rows) || rows.Count != 1)
            {
                Logger.Error(
                    "Restored mentoring quest id={0} has {1} distinct highlighted chest objective(s); expected one",
                    questId,
                    rows?.Count ?? 0);
                if (rows != null)
                {
                    foreach (var invalidRow in rows)
                        invalidChests.Add(invalidRow.ChestId);
                }
                continue;
            }

            var row = rows.Single();
            if (chestZones.TryGetValue(row.ChestId, out var existing) && existing != row)
            {
                Logger.Error(
                    "Restored mentoring chest doodad={0} has conflicting zone/initial phases {1}/{2} and {3}/{4}",
                    row.ChestId,
                    existing.ZoneGroupId,
                    existing.InitialPhaseId,
                    row.ZoneGroupId,
                    row.InitialPhaseId);
                invalidChests.Add(row.ChestId);
                continue;
            }

            chestZones[row.ChestId] = row;
        }

        foreach (var chest in invalidChests)
            chestZones.Remove(chest);
        return chestZones;
    }

    private static void LoadIndunActionCandidates(
        SqliteConnection connection,
        Dictionary<uint, MentoringChestSource> chestZones,
        Dictionary<(uint ZoneGroupId, uint NpcId), HashSet<(uint ChestId, uint PhaseId)>> candidates)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE action_chain(zone_group_id, npc_id, action_id) AS (
                SELECT event.zone_group_id, killed.npc_id, event.start_action_id
                  FROM indun_events event
                  JOIN indun_event_npc_killeds killed
                    ON event.condition_type = 'IndunEventNpcKilled'
                   AND killed.id = event.condition_id
                 WHERE event.start_action_id > 0
                UNION
                SELECT chain.zone_group_id, chain.npc_id, action.next_action_id
                  FROM action_chain chain
                  JOIN indun_actions action ON action.id = chain.action_id
                 WHERE action.next_action_id > 0
            )
            SELECT chain.zone_group_id, chain.npc_id,
                   changed.doodad_almighty_id AS doodad_id,
                   changed.doodad_func_group_id AS phase_id
              FROM action_chain chain
              JOIN indun_actions action ON action.id = chain.action_id
              JOIN indun_action_change_doodad_phases changed
                ON action.detail_type = 'IndunActionChangeDoodadPhase'
               AND changed.id = action.detail_id
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var zoneGroupId = reader.GetUInt32("zone_group_id");
            var chestId = reader.GetUInt32("doodad_id");
            if (!chestZones.TryGetValue(chestId, out var chest) || chest.ZoneGroupId != zoneGroupId)
                continue;

            AddCandidate(
                candidates,
                zoneGroupId,
                reader.GetUInt32("npc_id"),
                chestId,
                reader.GetUInt32("phase_id"));
        }
    }

    private static void LoadOnDeathSkillCandidates(
        SqliteConnection connection,
        Dictionary<uint, MentoringChestSource> chestZones,
        Dictionary<(uint ZoneGroupId, uint NpcId), HashSet<(uint ChestId, uint PhaseId)>> candidates)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT npc_skill.owner_id AS npc_id,
                   func_group.doodad_almighty_id AS doodad_id,
                   func.doodad_func_group_id AS source_phase_id,
                   func.next_phase AS phase_id
              FROM np_skills npc_skill
              JOIN doodad_func_skill_hits skill_hit ON skill_hit.skill_id = npc_skill.skill_id
              JOIN doodad_funcs func
                ON func.actual_func_type = 'DoodadFuncSkillHit'
               AND func.actual_func_id = skill_hit.id
              JOIN doodad_func_groups func_group ON func_group.id = func.doodad_func_group_id
             WHERE npc_skill.owner_type = 'Npc'
               AND npc_skill.skill_use_condition_id = @onDeath
               AND func.next_phase > 0
            """;
        command.Parameters.AddWithValue("@onDeath", (uint)SkillUseConditionKind.OnDeath);
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var chestId = reader.GetUInt32("doodad_id");
            if (!chestZones.TryGetValue(chestId, out var chest)
                || reader.GetUInt32("source_phase_id") != chest.InitialPhaseId)
                continue;

            AddCandidate(
                candidates,
                chest.ZoneGroupId,
                reader.GetUInt32("npc_id"),
                chestId,
                reader.GetUInt32("phase_id"));
        }
    }

    private static void AddCandidate(
        Dictionary<(uint ZoneGroupId, uint NpcId), HashSet<(uint ChestId, uint PhaseId)>> candidates,
        uint zoneGroupId,
        uint npcId,
        uint chestId,
        uint phaseId)
    {
        if (zoneGroupId == 0 || npcId == 0 || chestId == 0 || phaseId == 0)
            return;

        var key = (zoneGroupId, npcId);
        if (!candidates.TryGetValue(key, out var values))
        {
            values = [];
            candidates[key] = values;
        }

        values.Add((chestId, phaseId));
    }
}

public sealed record MentoringChestTrigger(
    uint ZoneGroupId,
    uint NpcTemplateId,
    uint DoodadTemplateId,
    uint InitialPhaseId,
    uint ExposedPhaseId);

internal sealed record MentoringChestSource(uint ChestId, uint ZoneGroupId, uint InitialPhaseId);
