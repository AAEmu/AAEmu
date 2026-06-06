using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
// ReSharper disable once ClassNeverInstantiated.Global
public class NpcGameData : Singleton<NpcGameData>, IGameDataLoader
{
    /// <summary>
    /// List of skill for NpcTemplateIds
    /// </summary>
    private Dictionary<uint, List<NpcSkill>> SkillsForNpc { get; } = [];
    /// <summary>
    /// Passive buffs for NpcTemplateid
    /// </summary>
    private Dictionary<uint, List<NpcPassiveBuff>> PassivesForNpc { get; } = [];
    /// <summary>
    /// List of NpcSpawnerNpcs by NpcSpawnerId
    /// </summary>
    private Dictionary<uint, NpcSpawnerNpc> NpcSpawnerTemplateNpcs { get; } = [];
    /// <summary>
    /// List of NpcSpawnerTemplates by Id
    /// </summary>
    private Dictionary<uint, NpcSpawnerTemplate> NpcSpawnerTemplates { get; } = [];
    /// <summary>
    /// List of SpawnerTemplateIds grouped by NpcTempalteId
    /// </summary>
    private Dictionary<uint, List<uint>> NpcMemberAndSpawnerTemplateIds { get; } = [];
    /// <summary>
    /// Skill list grouped by npc_interaction_set_id. Drives CSStartInteractionPacket — when a
    /// player right-clicks an NPC whose template has a non-zero NpcInteractionSetId, the server
    /// returns this list of skill IDs and the client renders a button per skill (e.g. Halcyona
    /// War Golem "기동" + "무기 장착").
    /// </summary>
    private Dictionary<uint, List<uint>> NpcInteractionSetSkills { get; } = [];

    /// <summary>
    /// Loads static Npc related data
    /// </summary>
    /// <param name="connection"></param>
    public void Load(SqliteConnection connection)
    {
        SkillsForNpc.Clear();
        PassivesForNpc.Clear();
        NpcSpawnerTemplateNpcs.Clear();
        NpcSpawnerTemplates.Clear();
        NpcMemberAndSpawnerTemplateIds.Clear();
        NpcInteractionSetSkills.Clear();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM np_skills";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcSkill
                {
                    Id = reader.GetUInt32("id"),
                    OwnerId = reader.GetUInt32("owner_id"),
                    OwnerType = reader.GetString("owner_type"),
                    SkillId = reader.GetUInt32("skill_id"),
                    SkillUseCondition = (SkillUseConditionKind)reader.GetUInt32("skill_use_condition_id"),
                    SkillUseParam1 = reader.GetFloat("skill_use_param1"),
                    SkillUseParam2 = reader.GetFloat("skill_use_param2")
                };

                if (!SkillsForNpc.ContainsKey(template.OwnerId))
                    SkillsForNpc.Add(template.OwnerId, []);

                SkillsForNpc[template.OwnerId].Add(template);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM np_passive_buffs";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcPassiveBuff
                {
                    Id = reader.GetUInt32("id"),
                    OwnerId = reader.GetUInt32("owner_id"),
                    OwnerType = reader.GetString("owner_type"),
                    PassiveBuffId = reader.GetUInt32("passive_buff_id")
                };

                if (!PassivesForNpc.ContainsKey(template.OwnerId))
                    PassivesForNpc.Add(template.OwnerId, []);

                PassivesForNpc[template.OwnerId].Add(template);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_spawners";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcSpawnerTemplate
                {
                    Id = reader.GetUInt32("id"), // matches NpcSpawnerTemplateId
                    NpcSpawnerCategoryId = (NpcSpawnerCategory)reader.GetUInt32("npc_spawner_category_id"),
                    Name = reader.GetString("name"),
                    Comment = reader.GetString("comment", ""),
                    MaxPopulation = reader.GetUInt32("maxPopulation"),
                    StartTime = reader.GetFloat("startTime"),
                    EndTime = reader.GetFloat("endTime"),
                    DestroyTime = reader.GetFloat("destroyTime"),
                    SpawnDelayMin = reader.GetFloat("spawn_delay_min"),
                    ActivationState = reader.GetBoolean("activation_state", true),
                    SaveIndun = reader.GetBoolean("save_indun", true),
                    MinPopulation = reader.GetUInt32("min_population"),
                    TestRadiusNpc = reader.GetFloat("test_radius_npc"),
                    TestRadiusPc = reader.GetFloat("test_radius_pc"),
                    SuspendSpawnCount = reader.GetUInt32("suspend_spawn_count"),
                    SpawnDelayMax = reader.GetFloat("spawn_delay_max"),
                    Npcs = []
                };
                NpcSpawnerTemplates.Add(template.Id, template);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_spawner_npcs";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var nsn = new NpcSpawnerNpc
                {
                    Id = reader.GetUInt32("id"),
                    NpcSpawnerTemplateId = reader.GetUInt32("npc_spawner_id"),
                    MemberId = reader.GetUInt32("member_id"),
                    MemberType = reader.GetString("member_type"),
                    Weight = reader.GetFloat("weight")
                };

                NpcSpawnerTemplateNpcs.Add(nsn.Id, nsn);
                NpcSpawnerTemplates[nsn.NpcSpawnerTemplateId].Npcs.Add(nsn);
            }
        }

        // npc_interactions: each row binds a skill_id to an npc_interaction_set_id. NPCs reference
        // a set via npcs.npc_interaction_set_id and we surface those skills on right-click via
        // CSStartInteractionPacket. Halcyona War Golem uses sets 31/32 with 2 skills each
        // (Mobilize + Equip Weapon), but the same wiring powers every interactable boss/quest NPC.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT npc_interaction_set_id, skill_id FROM npc_interactions";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var setId = reader.GetUInt32("npc_interaction_set_id");
                var skillId = reader.GetUInt32("skill_id");
                if (skillId == 0 || setId == 0)
                    continue;
                if (!NpcInteractionSetSkills.TryGetValue(setId, out var list))
                {
                    list = [];
                    NpcInteractionSetSkills[setId] = list;
                }
                list.Add(skillId);
            }
        }
    }

    /// <summary>
    /// Process data after all other data has been loaded
    /// </summary>
    public void PostLoad()
    {
        foreach (var (templateId, skills) in SkillsForNpc)
        {
            NpcManager.Instance.BindSkillsToTemplate(templateId, skills);
        }

        foreach (var passiveBuff in PassivesForNpc.Values.SelectMany(i => i))
        {
            if (passiveBuff.PassiveBuff != null)
                continue;
            passiveBuff.PassiveBuff = SkillManager.Instance.GetPassiveBuffTemplate(passiveBuff.PassiveBuffId);
        }

        foreach (var (templateId, passives) in PassivesForNpc)
        {
            var template = NpcManager.Instance.GetTemplate(templateId);
            template?.PassiveBuffs.AddRange(passives);
        }

        LoadMemberAndSpawnerTemplateIds();
    }

    /// <summary>
    /// Creates a cahced list of NpcSpawners by NpcTemplateId
    /// </summary>
    private void LoadMemberAndSpawnerTemplateIds()
    {
        NpcMemberAndSpawnerTemplateIds.Clear();

        foreach (var (_, npcSpawnerNpc) in NpcSpawnerTemplateNpcs)
        {
            if (!NpcMemberAndSpawnerTemplateIds.TryGetValue(npcSpawnerNpc.MemberId, out var value))
            {
                NpcMemberAndSpawnerTemplateIds.Add(npcSpawnerNpc.MemberId, [npcSpawnerNpc.NpcSpawnerTemplateId]);
            }
            else
            {
                value.Add(npcSpawnerNpc.NpcSpawnerTemplateId);
            }
        }
    }

    /// <summary>
    /// Gets a list of SpawnerIds for a given NpcTemplateId
    /// </summary>
    /// <param name="memberId"></param>
    /// <returns></returns>
    public List<uint> GetSpawnerIds(uint memberId)
    {
        return NpcMemberAndSpawnerTemplateIds.GetValueOrDefault(memberId);
    }

    /// <summary>
    /// Returns a NpcSpawnerTemplate for a given Id
    /// </summary>
    /// <param name="npcSpawnerTemplateId"></param>
    /// <returns></returns>
    public NpcSpawnerTemplate GetNpcSpawnerTemplate(uint npcSpawnerTemplateId)
    {
        return NpcSpawnerTemplates.GetValueOrDefault(npcSpawnerTemplateId);
    }

    /// <summary>
    /// Returns the skill list for an npc_interaction_set_id, or null if the set is unknown / empty.
    /// Order matches DB row order (which is the order the client renders the buttons in).
    /// </summary>
    public List<uint> GetNpcInteractionSetSkills(uint setId)
    {
        return setId == 0 ? null : NpcInteractionSetSkills.GetValueOrDefault(setId);
    }

    /// <summary>
    /// Rturns the first NpcSpawnerNpc from a given NpcSpawnerTemplateId
    /// </summary>
    /// <param name="spawnerId"></param>
    /// <returns></returns>
    public NpcSpawnerNpc GetNpcSpawnerNpc(uint spawnerId)
    {
        return NpcSpawnerTemplateNpcs.Values.FirstOrDefault(nsn => nsn.NpcSpawnerTemplateId == spawnerId);
    }

    /// <summary>
    /// Gets Non-Player skills list for a given NpcTempalte and situational trigger/condition
    /// </summary>
    /// <param name="npcId"></param>
    /// <param name="skillCondition"></param>
    /// <returns></returns>
    public List<NpcSkill> GetNpSkills(uint npcId, SkillUseConditionKind skillCondition = SkillUseConditionKind.None)
    {
        if (SkillsForNpc.TryGetValue(npcId, out var value))
        {
            if (skillCondition == SkillUseConditionKind.None)
                return SkillsForNpc[npcId];
            return value.Where(npSkill => npSkill.SkillUseCondition == skillCondition).ToList();
        }

        return null;
    }

    /// <summary>
    /// Register a NpcSpawnerTemplate
    /// </summary>
    /// <param name="template"></param>
    public void AddNpcSpawner(NpcSpawnerTemplate template)
    {
        NpcSpawnerTemplates.Add(template.Id, template);
    }

    /// <summary>
    /// Regsiters a NpcSpawnerNpc into cache
    /// </summary>
    /// <param name="nsn"></param>
    public void AddNpcSpawnerNpc(NpcSpawnerNpc nsn)
    {
        NpcSpawnerTemplateNpcs.Add(nsn.Id, nsn);
    }

    /// <summary>
    /// Registers a NpcSpawnerNpc to the cache of Npcs
    /// </summary>
    /// <param name="nsn"></param>
    public void AddMemberAndSpawnerTemplateIds(NpcSpawnerNpc nsn)
    {
        if (!NpcMemberAndSpawnerTemplateIds.TryGetValue(nsn.MemberId, out var npcMemberAndSpawnerTemplate))
            NpcMemberAndSpawnerTemplateIds.Add(nsn.MemberId, [nsn.NpcSpawnerTemplateId]);
        else
            npcMemberAndSpawnerTemplate.Add(nsn.NpcSpawnerTemplateId);
    }
}
