using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterSkills
    {
        private enum SkillType : byte
        {
            Skill = 1,
            Buff = 2
        }

        private List<uint> _removed;

        public Dictionary<uint, Skill> Skills { get; set; }
        public Dictionary<uint, PassiveBuff> PassiveBuffs { get; set; }

        public Character Owner { get; set; }

        public CharacterSkills(Character owner)
        {
            Owner = owner;
            Skills = new Dictionary<uint, Skill>();
            PassiveBuffs = new Dictionary<uint, PassiveBuff>();
            _removed = new List<uint>();
        }

        public void AddSkill(uint skillId)
        {
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            if (template.AbilityId > 0 &&
                template.AbilityId != (byte) Owner.Ability1 &&
                template.AbilityId != (byte) Owner.Ability2 &&
                template.AbilityId != (byte) Owner.Ability3)
                return;
            var points = ExpirienceManager.Instance.GetSkillPointsForLevel(Owner.Level);
            points -= GetUsedSkillPoints();
            if (template.SkillPoints > points)
                return;

            if (Skills.ContainsKey(skillId))
            {
                Skills[skillId].Level++;
                Owner.SendPacket(new SCSkillUpgradedPacket(Skills[skillId]));
            }
            else
                AddSkill(template, 1, true);
        }

        public void AddSkill(SkillTemplate template, byte level, bool packet)
        {
            var skill = new Skill
            {
                Id = template.Id,
                Template = template,
                Level = level
            };
            Skills.Add(skill.Id, skill);

            if (packet)
                Owner.SendPacket(new SCSkillLearnedPacket(skill));
        }
        
        public void AddBuff(uint buffId)
        {
            var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
            if(template.AbilityId > 0 && 
               template.AbilityId != (byte)Owner.Ability1 && 
               template.AbilityId != (byte)Owner.Ability2 && 
               template.AbilityId != (byte)Owner.Ability3)
                return;
            var points = ExpirienceManager.Instance.GetSkillPointsForLevel(Owner.Level);
            points -= GetUsedSkillPoints();
            if(template.ReqPoints > points)
                return;
            if(PassiveBuffs.ContainsKey(buffId))
                return;
            var buff = new PassiveBuff();
            buff.Id = buffId;
            buff.Template = template;
            PassiveBuffs.Add(buff.Id, buff);
            Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id), true);
            // TODO apply buff effect
        }

        public void Reset(AbilityType abilityId) // TODO with price...
        {
            foreach (var skill in new List<Skill>(Skills.Values))
            {
                if (skill.Template.AbilityId != (byte)abilityId)
                    continue;
                Skills.Remove(skill.Id);
                _removed.Add(skill.Id);
            }

            foreach (var buff in new List<PassiveBuff>(PassiveBuffs.Values))
            {
                if (buff.Template.AbilityId != (byte)abilityId)
                    continue;
                PassiveBuffs.Remove(buff.Id);
                _removed.Add(buff.Id);
            }
            
            Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, abilityId), true);
        }

        public int GetUsedSkillPoints()
        {
            var points = 0;
            foreach (var skill in Skills.Values)
                points += skill.Template.SkillPoints;
            foreach (var buff in PassiveBuffs.Values)
                points += buff.Template?.ReqPoints ?? 1;
            return points;
        }
        
        // TODO : Optimize this by storing a map of derivative skills and their matches
        public bool IsVariantOfSkill(uint skillId)
        {
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);

            return Skills.Values.Any(skill =>
                skill.Template.AbilityId == skillTemplate.AbilityId &&
                skill.Template.AbilityLevel == skillTemplate.AbilityLevel);
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skills WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var type = (SkillType) Enum.Parse(typeof(SkillType), reader.GetString("type"), true);
                        switch (type)
                        {
                            case SkillType.Skill:
                                var skill = new Skill
                                {
                                    Id = reader.GetUInt32("id"),
                                    Level = reader.GetByte("level")
                                };
                                Skills.Add(skill.Id, skill);
                                break;
                            case SkillType.Buff:
                                var buff = new PassiveBuff {Id = reader.GetUInt32("id")};
                                PassiveBuffs.Add(buff.Id, buff);
                                break;
                        }
                    }
                }
            }

            foreach (var skill in Skills.Values)
                if (skill != null)
                    skill.Template = SkillManager.Instance.GetSkillTemplate(skill.Id);
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_removed.Count > 0)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "DELETE FROM skills WHERE owner = @owner AND id IN(" + string.Join(",", _removed) + ")";
                    command.Prepare();
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                    _removed.Clear();
                }
            }

            foreach (var skill in Skills.Values)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText =
                        "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (@id, @level, @type, @owner)";
                    command.Parameters.AddWithValue("@id", skill.Id);
                    command.Parameters.AddWithValue("@level", skill.Level);
                    command.Parameters.AddWithValue("@type", (byte) SkillType.Skill);
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                }
            }

            foreach (var buff in PassiveBuffs.Values)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText =
                        "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES(@id,@level,@type,@owner)";
                    command.Parameters.AddWithValue("@id", buff.Id);
                    command.Parameters.AddWithValue("@level", 1);
                    command.Parameters.AddWithValue("@type", (byte) SkillType.Buff);
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
