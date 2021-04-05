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
        
        public void Load(SqliteConnection connection)
        {
            _skillsForNpc = new Dictionary<uint, List<NpcSkill>>();
            _passivesForNpc = new Dictionary<uint, List<NpcPassiveBuff>>();
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM np_skills";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
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
            }
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM np_passive_buffs";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
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
    }
}
