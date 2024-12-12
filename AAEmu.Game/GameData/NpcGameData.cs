using System.Collections.Generic;
using System.Linq;

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
public class NpcGameData : Singleton<NpcGameData>, IGameDataLoader
{
    private Dictionary<uint, List<NpcSkill>> _skillsForNpc;
    private Dictionary<uint, List<NpcPassiveBuff>> _passivesForNpc;
    public Dictionary<uint, NpcSpawnerNpc> _npcSpawnerTemplateNpcs;      // Id, nsn
    public Dictionary<uint, NpcSpawnerTemplate> _npcSpawnerTemplates;    // NpcSpawnerTemplateId, template
    public Dictionary<uint, List<uint>> _npcMemberAndSpawnerTemplateIds; // memberId, List<npcSpawnerId>

    public void Load(SqliteConnection connection)
    {
        _skillsForNpc = [];
        _passivesForNpc = [];
        _npcSpawnerTemplateNpcs = [];
        _npcSpawnerTemplates = [];
        _npcMemberAndSpawnerTemplateIds = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM np_skills";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcSkill()
                {
                    Id = reader.GetUInt32("id"),
                    OwnerId = reader.GetUInt32("owner_id"),
                    OwnerType = reader.GetString("owner_type"),
                    SkillId = reader.GetUInt32("skill_id"),
                    SkillUseCondition = (SkillUseConditionKind)reader.GetUInt32("skill_use_condition_id"),
                    SkillUseParam1 = reader.GetFloat("skill_use_param1"),
                    SkillUseParam2 = reader.GetFloat("skill_use_param2")
                };

                if (!_skillsForNpc.ContainsKey(template.OwnerId))
                    _skillsForNpc.Add(template.OwnerId, []);

                _skillsForNpc[template.OwnerId].Add(template);
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
                var template = new NpcPassiveBuff()
                {
                    Id = reader.GetUInt32("id"),
                    OwnerId = reader.GetUInt32("owner_id"),
                    OwnerType = reader.GetString("owner_type"),
                    PassiveBuffId = reader.GetUInt32("passive_buff_id")
                };

                if (!_passivesForNpc.ContainsKey(template.OwnerId))
                    _passivesForNpc.Add(template.OwnerId, []);

                _passivesForNpc[template.OwnerId].Add(template);
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
                var template = new NpcSpawnerTemplate();
                template.Id = reader.GetUInt32("id"); // matches NpcSpawnerTemplateId
                template.NpcSpawnerCategoryId = (NpcSpawnerCategory)reader.GetUInt32("npc_spawner_category_id");
                template.Name = reader.GetString("name");
                template.Comment = reader.GetString("comment", "");
                template.MaxPopulation = reader.GetUInt32("maxPopulation");
                template.StartTime = reader.GetFloat("startTime");
                template.EndTime = reader.GetFloat("endTime");
                template.DestroyTime = reader.GetFloat("destroyTime");
                template.SpawnDelayMin = reader.GetFloat("spawn_delay_min");
                template.ActivationState = reader.GetBoolean("activation_state");
                template.SaveIndun = reader.GetBoolean("save_indun");
                template.MinPopulation = reader.GetUInt32("min_population");
                template.TestRadiusNpc = reader.GetFloat("test_radius_npc");
                template.TestRadiusPc = reader.GetFloat("test_radius_pc");
                template.SuspendSpawnCount = reader.GetUInt32("suspend_spawn_count");
                template.SpawnDelayMax = reader.GetFloat("spawn_delay_max");
                template.Npcs = [];
                _npcSpawnerTemplates.Add(template.Id, template);
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
                var nsn = new NpcSpawnerNpc();
                nsn.Id = reader.GetUInt32("id");
                nsn.NpcSpawnerTemplateId = reader.GetUInt32("npc_spawner_id");
                nsn.MemberId = reader.GetUInt32("member_id");
                nsn.MemberType = reader.GetString("member_type");
                nsn.Weight = reader.GetFloat("weight");

                _npcSpawnerTemplateNpcs.Add(nsn.Id, nsn);
                _npcSpawnerTemplates[nsn.NpcSpawnerTemplateId].Npcs.Add(nsn);
            }
        }
    }

    public void PostLoad()
    {
        foreach (var (templateId, skills) in _skillsForNpc)
        {
            NpcManager.Instance.BindSkillsToTemplate(templateId, skills);
        }

        foreach (var passiveBuff in _passivesForNpc.Values.SelectMany(i => i))
        {
            if (passiveBuff.PassiveBuff != null)
                continue;
            passiveBuff.PassiveBuff = SkillManager.Instance.GetPassiveBuffTemplate(passiveBuff.PassiveBuffId);
        }

        foreach (var (templateId, passives) in _passivesForNpc)
        {
            var template = NpcManager.Instance.GetTemplate(templateId);
            template?.PassiveBuffs.AddRange(passives);
        }
    }

    public void LoadMemberAndSpawnerTemplateIds()
    {
        _npcMemberAndSpawnerTemplateIds = [];
        var npcMemberAndSpawnerId = new Dictionary<uint, List<uint>>();

        foreach (var nsn in _npcSpawnerTemplateNpcs.Values)
        {
            if (!_npcMemberAndSpawnerTemplateIds.TryGetValue(nsn.MemberId, out var value))
            {
                _npcMemberAndSpawnerTemplateIds.Add(nsn.MemberId, [nsn.NpcSpawnerTemplateId]);
            }
            else
            {
                value.Add(nsn.NpcSpawnerTemplateId);
            }
        }
    }

    public List<uint> GetSpawnerIds(uint memberId)
    {
        _npcMemberAndSpawnerTemplateIds.TryGetValue(memberId, out var list);

        return list;
    }

    public NpcSpawnerTemplate GetNpcSpawnerTemplate(uint npcSpawnerTemplateId)
    {
        _npcSpawnerTemplates.TryGetValue(npcSpawnerTemplateId, out var template);

        return template;
    }

    public NpcSpawnerNpc GetNpcSpawnerNpc(uint spawnerId)
    {
        //_npcSpawnerTemplateNpcs.TryGetValue(spawnerId, out var nsn);
        return _npcSpawnerTemplateNpcs.Values.FirstOrDefault(nsn => nsn.NpcSpawnerTemplateId == spawnerId);
    }

    public List<NpcSkill> GetNpSkills(uint npcId, SkillUseConditionKind skillCondition = SkillUseConditionKind.None)
    {
        if (_skillsForNpc.TryGetValue(npcId, out var value))
        {
            if (skillCondition == SkillUseConditionKind.None)
                return _skillsForNpc[npcId];
            return value.Where(npSkill => npSkill.SkillUseCondition == skillCondition).ToList();
        }

        return null;
    }
}
