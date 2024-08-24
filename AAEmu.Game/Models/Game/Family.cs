using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game;

public class Family : PacketMarshaler
{
    private List<uint> _removedMembers;

    public uint Id { get; init; }
    public List<FamilyMember> Members { get; } = new();

    public Family()
    {
        _removedMembers = new List<uint>();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(Members.Count); // TODO max length 8
        foreach (var member in Members)
            stream.Write(member);
        return stream;
    }

    public void AddMember(FamilyMember member)
    {
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
        character.Family = 0;
    }

    public FamilyMember GetMember(Character character)
    {
        foreach (var member in Members)
            if (member.Id == character.Id)
                return member;

        return null;
    }

    public void SendPacket(GamePacket packet, uint exclude = 0)
    {
        foreach (var member in Members)
            if (member.Id != exclude)
                member.Character?.SendPacket(packet);
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM family_members WHERE family_id=@family_id";
            command.Parameters.AddWithValue("family_id", Id);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var member = new FamilyMember();
                    member.Id = reader.GetUInt32("character_id");
                    member.Name = reader.GetString("name");
                    member.Role = reader.GetByte("role");
                    member.Title = reader.GetString("title");
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
                command.Parameters.AddWithValue("@family_id", Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = $"UPDATE characters SET family = 0 WHERE `characters`.`id` IN ({removedMembers})";
                command.Parameters.AddWithValue("@family_id", Id);
                command.Prepare();
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
        stream.Write(Id);
        stream.Write(Name);
        stream.Write(Role);
        stream.Write(Online);
        stream.Write(Title);
        return stream;
    }
}
