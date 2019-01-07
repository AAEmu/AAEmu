using System.Collections.Generic;
using AAEmu.Game.Models.Game.Skills;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterAbilities
    {
        public Dictionary<AbilityType, Ability> Abilities { get; set; }
        public Character Owner { get; set; }

        public CharacterAbilities(Character owner)
        {
            Owner = owner;
            Abilities = new Dictionary<AbilityType, Ability>();
            for (var i = 1; i < 11; i++)
            {
                var id = (AbilityType) i;
                Abilities[id] = new Ability(id);
            }
        }

        public IEnumerable<Ability> Values => Abilities.Values;

        public void SetAbility(AbilityType id, byte order)
        {
            Abilities[id].Order = order;
        }

        public List<AbilityType> GetActiveAbilities()
        {
            var list = new List<AbilityType>();
            if (Owner.Ability1 != AbilityType.None)
                list.Add(Owner.Ability1);
            if (Owner.Ability2 != AbilityType.None)
                list.Add(Owner.Ability2);
            if (Owner.Ability3 != AbilityType.None)
                list.Add(Owner.Ability3);
            return list;
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM abilities WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var ability = new Ability
                        {
                            Id = (AbilityType) reader.GetByte("id"),
                            Exp = reader.GetInt32("exp")
                        };
                        if (ability.Id == Owner.Ability1)
                            ability.Order = 0;
                        if (ability.Id == Owner.Ability2)
                            ability.Order = 1;
                        if (ability.Id == Owner.Ability3)
                            ability.Order = 2;
                        Abilities[ability.Id] = ability;
                    }
                }
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            foreach (var ability in Abilities.Values)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "REPLACE INTO abilities(`id`,`exp`,`owner`) VALUES (@id, @exp, @owner)";
                    command.Parameters.AddWithValue("@id", (byte) ability.Id);
                    command.Parameters.AddWithValue("@exp", ability.Exp);
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}