using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
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

        public Dictionary<uint, Skill> Skills { get; set; }
        public Dictionary<uint, PassiveBuff> PassiveBuffs { get; set; }

        public Character Owner { get; set; }

        public CharacterSkills(Character owner)
        {
            Owner = owner;
            Skills = new Dictionary<uint, Skill>();
            PassiveBuffs = new Dictionary<uint, PassiveBuff>();
        }

        public void AddSkill(SkillTemplate template, byte level)
        {
            var skill = new Skill
            {
                Id = template.Id,
                Template = template,
                Level = level
            };
            Skills.Add(skill.Id, skill);
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