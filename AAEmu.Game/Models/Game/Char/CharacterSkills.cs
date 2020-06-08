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
        public Dictionary<uint, PassiveBuffTemplate> PassiveBuffs { get; set; }

        public Character Owner { get; set; }

        public CharacterSkills(Character owner)
        {
            Owner = owner;
            Skills = new Dictionary<uint, Skill>();
            PassiveBuffs = new Dictionary<uint, PassiveBuffTemplate>();
            _removed = new List<uint>();
        }

        public void AddSkill(uint skillId)
        {
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            if (template.AbilityId > 0 &&
                template.AbilityId != (byte)Owner.Ability1 &&
                template.AbilityId != (byte)Owner.Ability2 &&
                template.AbilityId != (byte)Owner.Ability3)
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
        public void AddSkill(uint skillId, bool newlyLearned)
        {
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            if (template.AbilityId > 10 || template.SkillPoints > GetRemainingSkillPoints())
                return;

            int skillTreeLevel = -1;
            if ((byte)Owner.Ability1 == template.AbilityId)
            {
                skillTreeLevel =
                    ExpirienceManager.Instance.GetLevelFromExp(Owner.Abilities.Abilities[Owner.Ability1].Exp);
            }
            else if ((byte)Owner.Ability2 == template.AbilityId)
            {
                skillTreeLevel =
                    ExpirienceManager.Instance.GetLevelFromExp(Owner.Abilities.Abilities[Owner.Ability2].Exp);
            }
            else if ((byte)Owner.Ability3 == template.AbilityId)
            {
                skillTreeLevel =
                    ExpirienceManager.Instance.GetLevelFromExp(Owner.Abilities.Abilities[Owner.Ability3].Exp);
            }

            if (Owner.Level >= 10 && skillTreeLevel < 10)
            {
                skillTreeLevel = 10;
            }
            else if (Owner.Level < 10)
            {
                skillTreeLevel = Owner.Level;
            }

            if (skillTreeLevel != -1 && skillTreeLevel >= template.AbilityLevel)
            {
                byte skillLevel = 1;
                if (template.LevelStep != 0)
                {
                    skillLevel += (byte)((skillTreeLevel - template.AbilityLevel) / template.LevelStep);
                }

                var skill = new Skill
                {
                    Id = template.Id,
                    Template = template,
                    Level = skillLevel
                };

                Skills.Add(skill.Id, skill);

                if (newlyLearned)
                {
                    Owner.SendPacket(new SCSkillLearnedPacket(skill));
                }
            }
        }

        public void AddBuff(uint buffId)
        {
            var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
            if (template.AbilityId > 0 &&
               template.AbilityId != (byte)Owner.Ability1 &&
               template.AbilityId != (byte)Owner.Ability2 &&
               template.AbilityId != (byte)Owner.Ability3)
                return;
            var points = ExpirienceManager.Instance.GetSkillPointsForLevel(Owner.Level);
            points -= GetUsedSkillPoints();
            if (template.ReqPoints > points)
                return;
            if (PassiveBuffs.ContainsKey(buffId))
                return;
            var buff = new PassiveBuffTemplate();
            buff.Id = buffId;
            buff.Template = template;
            PassiveBuffs.Add(buff.Id, buff);
            Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id), true);
            // TODO apply buff effect
        }

        public void AddPassive(uint passiveId, bool newlyLearned)
        {
            var template = SkillManager.Instance.GetPassiveBuffTemplate(passiveId);
            if (template.AbilityId > 10 || GetRemainingSkillPoints() < 1 || PassiveBuffs.ContainsKey(template.BuffId))
                return;

            int activeSkillsInTree = 0;
            foreach (var skill in Skills.Values)
                if (skill.Template.AbilityId == template.AbilityId)
                    activeSkillsInTree++;

            foreach (var passive in PassiveBuffs.Values)
                if (passive.AbilityId == template.AbilityId)
                    activeSkillsInTree++;

            if (newlyLearned && activeSkillsInTree < template.ReqPoints)
                return;

            PassiveBuffs.Add(passiveId, template);

            if (newlyLearned)
                Owner.SendPacket(new SCBuffLearnedPacket(Owner.ObjId, passiveId));


            if (!Owner.Effects.CheckBuff(template.BuffId))
            {
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(template.BuffId);
                buffTemplate.Kind = BuffKind.Hidden; //TODO: change all passive buffs in SQLite db's (client && server) to be hidden so they don't appear on the buff bar
                Owner.Effects.AddEffect(new Effect(Owner, Owner, SkillCaster.GetByType(SkillCasterType.Unit),
                    buffTemplate, null, DateTime.Now));
            }
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

            foreach (var buff in new List<PassiveBuffTemplate>(PassiveBuffs.Values))
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
                points += buff.Template.ReqPoints;
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

        public int GetRemainingSkillPoints()
        {
            return ExpirienceManager.Instance.GetSkillPointsForLevel(Owner.Level) - GetUsedSkillPoints();
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
                        var type = (SkillType)Enum.Parse(typeof(SkillType), reader.GetString("type"), true);
                        switch (type)
                        {
                            case SkillType.Skill:
                                AddSkill(reader.GetUInt32("id"), false);
                                break;
                            case SkillType.Buff:
                                AddPassive(reader.GetUInt32("id"), false);
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
                    command.Parameters.AddWithValue("@type", (byte)SkillType.Skill);
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
                    command.Parameters.AddWithValue("@type", (byte)SkillType.Buff);
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
