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

namespace AAEmu.Game.GameData
{
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
            _skillsForNpc = new Dictionary<uint, List<NpcSkill>>();
            _passivesForNpc = new Dictionary<uint, List<NpcPassiveBuff>>();
            _npcSpawnerTemplateNpcs = new Dictionary<uint, NpcSpawnerNpc>();
            _npcSpawnerTemplates = new Dictionary<uint, NpcSpawnerTemplate>();
            _npcMemberAndSpawnerTemplateIds = new Dictionary<uint, List<uint>>();

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
                        _skillsForNpc.Add(template.OwnerId, new List<NpcSkill>());

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
                        _passivesForNpc.Add(template.OwnerId, new List<NpcPassiveBuff>());

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
                    template.Npcs = new List<NpcSpawnerNpc>();
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
            _npcMemberAndSpawnerTemplateIds = new Dictionary<uint, List<uint>>();
            var npcMemberAndSpawnerId = new Dictionary<uint, List<uint>>();

            foreach (var nsn in _npcSpawnerTemplateNpcs.Values)
            {
                if (!_npcMemberAndSpawnerTemplateIds.ContainsKey(nsn.MemberId))
                {
                    _npcMemberAndSpawnerTemplateIds.Add(nsn.MemberId, new List<uint> { nsn.NpcSpawnerTemplateId });
                }
                else
                {
                    _npcMemberAndSpawnerTemplateIds[nsn.MemberId].Add(nsn.NpcSpawnerTemplateId);
                }
            }

            foreach (var (memberId, spawnerTemplateIds) in _npcMemberAndSpawnerTemplateIds)
            {
                if (spawnerTemplateIds.Count <= 1)
                {
                    npcMemberAndSpawnerId.Add(memberId, spawnerTemplateIds);
                    continue;
                }
                var itemAutocreated = spawnerTemplateIds.Where(spawnerTemplateId =>
                    _npcSpawnerTemplates[spawnerTemplateId].NpcSpawnerCategoryId == NpcSpawnerCategory.Autocreated)
                        .ToList();

                //var itemNormal = slist.Where(t => _npcSpawners[t].NpcSpawnerCategoryId == NpcSpawnerCategory.Normal).ToList();

                var itemSpawnerSchedule = spawnerTemplateIds.Where(spawnerTemplateId =>
                    _npcSpawnerTemplates[spawnerTemplateId].StartTime != 0
                    && _npcSpawnerTemplates[spawnerTemplateId].EndTime != 0)
                        .ToList();

                var itemGameSchedule = spawnerTemplateIds.Where(t => GameScheduleManager.Instance.HasGameScheduleSpawnersData(t)).ToList();

                if (itemSpawnerSchedule.Count == 0 && itemGameSchedule.Count == 0)
                {
                    // если нет в расписаниях, то оставим только Autocreated
                    // if it's not on the schedules, then we'll just leave Autocreated
                    npcMemberAndSpawnerId.Add(memberId, itemAutocreated);
                }
                else if (itemSpawnerSchedule.Count > 0)
                {
                    // оставим только ту запись, которая есть в NpcSpawners
                    // leave only the entry that is in NpcSpawners
                    npcMemberAndSpawnerId.Add(memberId,
                        itemSpawnerSchedule.Count > 1 ? new List<uint> { itemSpawnerSchedule[0] } : itemSpawnerSchedule);
                }
                else if (itemGameSchedule.Count > 0)
                {
                    // оставим только ту запись, которая есть в GameScheduleSpawners
                    // we'll leave only the entry that is in GameScheduleSpawners
                    npcMemberAndSpawnerId.Add(memberId,
                        itemGameSchedule.Count > 1 ? new List<uint> { itemGameSchedule[0] } : itemGameSchedule);
                }
            }
            _npcMemberAndSpawnerTemplateIds = npcMemberAndSpawnerId;
        }

        public List<uint> GetSpawnerIds(uint memberId)
        {
            //return _npcMemberAndSpawnerTemplateIds.ContainsKey(memberId) ? _npcMemberAndSpawnerTemplateIds[memberId] : null;
            _npcMemberAndSpawnerTemplateIds.TryGetValue(memberId, out var list);

            return list;
        }

        public NpcSpawnerTemplate GetNpcSpawnerTemplate(uint npcSpawnerTemplateId)
        {
            //return _npcSpawnerTemplates.ContainsKey(npcSpawnerTemplateId) ? _npcSpawnerTemplates[npcSpawnerTemplateId] : null;
            _npcSpawnerTemplates.TryGetValue(npcSpawnerTemplateId, out var template);

            return template;
        }

        public NpcSpawnerNpc GetNpcSpawnerNpc(uint spawnerId)
        {
            //_npcSpawnerTemplateNpcs.TryGetValue(spawnerId, out var nsn);
            return _npcSpawnerTemplateNpcs.Values.FirstOrDefault(nsn => nsn.NpcSpawnerTemplateId == spawnerId);
        }
    }
}
