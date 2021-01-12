using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game
{
    public class Family : PacketMarshaler
    {
        private readonly List<uint> _removedMembers;

        public uint Id { get; set; }
        public List<FamilyMember> Members { get; set; }

        public Family()
        {
            _removedMembers = new List<uint>();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id); // family UInt32
            stream.Write(Members.Count); // TODO in 1.2 max length 8
            foreach (var member in Members)
            {
                stream.Write(member);
            }

            return stream;
        }

        public void AddMember(FamilyMember member)
        {
            if (Members == null)
            {
                Members = new List<FamilyMember>();
            }

            Members.Add(member);
        }

        public void RemoveMember(FamilyMember member)
        {
            Members.Remove(member);
            _removedMembers.Add(member.Id);
        }

        public void RemoveMember(Character character)
        {
            var member = GetMember(character);
            RemoveMember(member);
        }

        public FamilyMember GetMember(Character character)
        {
            foreach (var member in Members)
            {
                if (member.Id == character.Id)
                {
                    return member;
                }
            }

            return null;
        }

        public void SendPacket(GamePacket packet, uint exclude = 0)
        {
            foreach (var member in Members)
            {
                if (member.Id != exclude)
                {
                    member.Character?.SendPacket(packet);
                }
            }
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM family_members WHERE family_id=@family_id";
                command.Prepare();
                command.Parameters.AddWithValue("family_id", Id);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var member = new FamilyMember
                        {
                            Id = reader.GetUInt32("character_id"),
                            Name = reader.GetString("name"),
                            Role = reader.GetByte("role"),
                            Title = reader.GetString("title")
                        };
                        AddMember(member);
                    }
                }
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_removedMembers.Count > 0)
            {
                var removedMembers = string.Join(",", _removedMembers);

                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = $"DELETE FROM family_members WHERE character_id IN ({removedMembers})";
                    command.Prepare();
                    command.Parameters.AddWithValue("@family_id", Id);
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = $"UPDATE characters SET family = 0 WHERE `characters`.`id` IN ({removedMembers})";
                    command.Prepare();
                    command.Parameters.AddWithValue("@family_id", Id);
                    command.ExecuteNonQuery();
                }

                _removedMembers.Clear();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                foreach (var member in Members)
                {
                    command.CommandText = "REPLACE INTO " +
                                          "family_members(`character_id`,`family_id`,`name`,`role`,`title`)" +
                                          " VALUES " +
                                          "(@character_id,@family_id,@name,@role,@title)";
                    command.Parameters.AddWithValue("@character_id", member.Id);
                    command.Parameters.AddWithValue("@family_id", Id);
                    command.Parameters.AddWithValue("@name", member.Name);
                    command.Parameters.AddWithValue("@role", member.Role);
                    command.Parameters.AddWithValue("@title", member.Title);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }
        }
    }

    public class FamilyMember : PacketMarshaler
    {
        public Character Character { get; set; }

        public uint Id { get; set; }
        public string Name { get; set; }
        public byte Role { get; set; }
        public bool Online => Character != null;
        public string Title { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);   // member
            stream.Write(Name); // memberName
            stream.Write(Role);
            stream.Write(Online);
            stream.Write(Title);
            return stream;
        }
    }
}
