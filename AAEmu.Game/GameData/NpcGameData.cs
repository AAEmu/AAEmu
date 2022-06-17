using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.NPChar.NPSpawner;
using AAEmu.Game.Models.Game.Schedules;
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
        public Dictionary<uint, NpcSpawnerNpc> _npcSpawnerNpc;    // npcSpawnerId, nsn
        public Dictionary<uint, NpcSpawnerTemplate> _npcSpawners; // npcSpawnerId, template
        public Dictionary<uint, List<uint>> _npcMemberAndSpawnerId; // memberId, List<npcSpawnerId>

        public void Load(SqliteConnection connection)
        {
            _skillsForNpc = new Dictionary<uint, List<NpcSkill>>();
            _passivesForNpc = new Dictionary<uint, List<NpcPassiveBuff>>();
            _npcSpawnerNpc = new Dictionary<uint, NpcSpawnerNpc>();
            _npcSpawners = new Dictionary<uint, NpcSpawnerTemplate>();
            _npcMemberAndSpawnerId = new Dictionary<uint, List<uint>>();

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
                    template.Id = reader.GetUInt32("id");
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
                    _npcSpawners.Add(template.Id, template);
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
                    nsn.NpcSpawnerId = reader.GetUInt32("npc_spawner_id");
                    nsn.MemberId = reader.GetUInt32("member_id");
                    nsn.MemberType = reader.GetString("member_type");
                    nsn.Weight = reader.GetFloat("weight");

                    if (_npcSpawners.ContainsKey(nsn.NpcSpawnerId))
                    {
                        _npcSpawners[nsn.NpcSpawnerId].Npcs.Add(nsn);
                    }

                    _npcSpawnerNpc.Add(nsn.Id, nsn);
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

        public void GetMemberAndSpawnerId()
        {
            _npcMemberAndSpawnerId = new Dictionary<uint, List<uint>>();

            foreach (var (_, nsn) in _npcSpawnerNpc)
            {

                if (!_npcMemberAndSpawnerId.ContainsKey(nsn.MemberId))
                {
                    _npcMemberAndSpawnerId.Add(nsn.MemberId, new List<uint> { nsn.NpcSpawnerId });
                }
                else
                {
                    _npcMemberAndSpawnerId[nsn.MemberId].Add(nsn.NpcSpawnerId);
                }
            }

            foreach (var (_, slist) in _npcMemberAndSpawnerId)
            {
                if (slist.Count <= 1) { continue; }
                var item = slist.Where(t => _npcSpawners[t].NpcSpawnerCategoryId == NpcSpawnerCategory.Normal).ToList();
                var remove = slist.Where(t => !GameScheduleManager.Instance.GetGameScheduleSpawnersData(t)).ToList();
                if (remove.Count == slist.Count)
                {
                    // если вдруг все удалить надо, то оставим только Autocreated
                    remove = null;
                }

                if (remove != null)
                {
                    // оставим только ту запись, которая есть в GameScheduleSpawnrs
                    foreach (var i in remove)
                    {
                        slist.Remove(i);
                    }
                }
                else
                {
                    // оставим только запись Autocreated
                    foreach (var i in item)
                    {
                        slist.Remove(i);
                    }
                }
            }
        }

        public List<uint> GetSpawnersId(uint memberId)
        {
            return _npcMemberAndSpawnerId.ContainsKey(memberId) ? _npcMemberAndSpawnerId[memberId] : null;
        }

        public NpcSpawnerTemplate GetNpcSpawnerTemplate(uint npcSpawnerId)
        {
            return _npcSpawners.ContainsKey(npcSpawnerId) ? _npcSpawners[npcSpawnerId] : null;
        }
    }
}
