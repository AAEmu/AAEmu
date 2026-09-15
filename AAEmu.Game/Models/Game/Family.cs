using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game;

public class Family : PacketMarshaler
{
    private const int NativeMemberCapacity = 60;
    private readonly List<uint> _removedMembers = [];

    public uint Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Notice { get; set; } = string.Empty;
    public uint Level { get; set; } = 1;
    public uint Exp { get; set; }
    public uint IncreasedMemberCount { get; set; }
    public long ChangeNameTime { get; set; }
    public long ResetTime { get; set; }
    public Dictionary<byte, long> ActSanctions { get; } = [];
    public long RemovedMemberRejoinUntil { get; set; }
    public int MemberLimit => FamilyGameData.Instance.GetMemberLimit(IncreasedMemberCount);
    public List<FamilyMember> Members { get; } = [];

    public override PacketStream Write(PacketStream stream)
    {
        if (Members.Count > NativeMemberCapacity)
            throw new InvalidOperationException($"A family descriptor cannot contain more than {NativeMemberCapacity} members.");

        stream.Write(checked((int)Id));
        stream.Write((uint)Members.Count);
        foreach (var member in Members)
            stream.Write(member);
        stream.Write(Name);
        stream.Write(Level);
        stream.Write(Exp);
        stream.Write(Notice);
        stream.Write(IncreasedMemberCount);
        stream.Write(ResetTime);
        stream.Write(ChangeNameTime);
        stream.Write(ActSanctions.Count);
        foreach (var (type, endTime) in ActSanctions)
        {
            stream.Write(type);
            stream.Write(endTime);
        }
        return stream;
    }

    public void AddMember(FamilyMember member)
    {
        Members.Add(member);
    }

    public bool RemoveMember(FamilyMember member)
    {
        if (member == null || !Members.Remove(member))
            return false;

        _removedMembers.Add(member.Id);
        return true;
    }

    public void RestoreMember(FamilyMember member)
    {
        if (!Members.Contains(member))
            Members.Add(member);
        _removedMembers.Remove(member.Id);
    }

    public void RemoveMember(Character character)
    {
        var member = GetMember(character);
        if (RemoveMember(member))
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
            command.CommandText = "SELECT name,notice,level,exp,increased_member_count,reset_time,change_name_time FROM families WHERE id=@id";
            command.Parameters.AddWithValue("@id", Id);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                Name = reader.GetString("name");
                Notice = reader.GetString("notice");
                Level = reader.GetUInt32("level");
                Exp = reader.GetUInt32("exp");
                IncreasedMemberCount = reader.GetUInt32("increased_member_count");
                ResetTime = reader.GetInt64("reset_time");
                ChangeNameTime = reader.GetInt64("change_name_time");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT fm.*,c.level,c.heir_exp FROM family_members fm " +
                                  "JOIN characters c ON c.id=fm.character_id AND c.family=fm.family_id " +
                                  "WHERE fm.family_id=@family_id";
            command.Parameters.AddWithValue("family_id", Id);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var member = new FamilyMember
                    {
                        Id = reader.GetUInt32("character_id"), Name = reader.GetString("name"),
                        Level = reader.GetByte("level"),
                        HeirLevel = HeirGameData.Instance.GetLevelForExp(reader.GetInt64("heir_exp")),
                        Role = reader.GetByte("role"), RoleUpdateTime = reader.GetInt64("role_update_time"),
                        LoginRewardTime = reader.GetInt64("login_reward_time"),
                        Title = reader.GetString("title")
                    };
                    AddMember(member);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT type,end_time FROM family_act_sanctions WHERE family_id=@family_id";
            command.Parameters.AddWithValue("@family_id", Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                ActSanctions[reader.GetByte("type")] = reader.GetInt64("end_time");
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Members.Count == 0
                ? "DELETE FROM families WHERE id=@id"
                : "REPLACE INTO families(id,name,notice,level,exp,increased_member_count,reset_time,change_name_time) " +
                  "VALUES(@id,@name,@notice,@level,@exp,@increased_member_count,@reset_time,@change_name_time)";
            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@name", Name);
            command.Parameters.AddWithValue("@notice", Notice);
            command.Parameters.AddWithValue("@level", Level);
            command.Parameters.AddWithValue("@exp", Exp);
            command.Parameters.AddWithValue("@increased_member_count", IncreasedMemberCount);
            command.Parameters.AddWithValue("@reset_time", ResetTime);
            command.Parameters.AddWithValue("@change_name_time", ChangeNameTime);
            command.ExecuteNonQuery();
        }

        if (_removedMembers.Count > 0)
        {
            var removedMembers = string.Join(",", _removedMembers);

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = $"DELETE FROM family_members WHERE family_id=@family_id AND character_id IN ({removedMembers})";
                command.Parameters.AddWithValue("@family_id", Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = $"UPDATE characters SET family=0,family_rejoin_until=@rejoin_until " +
                                      $"WHERE family=@family_id AND `characters`.`id` IN ({removedMembers})";
                command.Parameters.AddWithValue("@family_id", Id);
                command.Parameters.AddWithValue("@rejoin_until", RemovedMemberRejoinUntil);
                command.Prepare();
                command.ExecuteNonQuery();
            }

        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;
            foreach (var member in Members)
            {
                command.CommandText = "REPLACE INTO " +
                                      "family_members(`character_id`,`family_id`,`name`,`role`,`role_update_time`,`login_reward_time`,`title`)" +
                                      " VALUES " +
                                      "(@character_id,@family_id,@name,@role,@role_update_time,@login_reward_time,@title)";
                command.Parameters.AddWithValue("@character_id", member.Id);
                command.Parameters.AddWithValue("@family_id", Id);
                command.Parameters.AddWithValue("@name", member.Name);
                command.Parameters.AddWithValue("@role", member.Role);
                command.Parameters.AddWithValue("@role_update_time", member.RoleUpdateTime);
                command.Parameters.AddWithValue("@login_reward_time", member.LoginRewardTime);
                command.Parameters.AddWithValue("@title", member.Title);
                command.ExecuteNonQuery();
                command.Parameters.Clear();

                command.CommandText = "UPDATE characters SET family=@family_id,family_rejoin_until=0 WHERE id=@character_id";
                command.Parameters.AddWithValue("@character_id", member.Id);
                command.Parameters.AddWithValue("@family_id", Id);
                command.ExecuteNonQuery();
                command.Parameters.Clear();
            }
        }


        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM family_act_sanctions WHERE family_id=@family_id";
            command.Parameters.AddWithValue("@family_id", Id);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            foreach (var (type, endTime) in Members.Count == 0
                         ? Enumerable.Empty<KeyValuePair<byte, long>>()
                         : ActSanctions)
            {
                command.CommandText = "INSERT INTO family_act_sanctions(family_id,type,end_time) VALUES(@family_id,@type,@end_time)";
                command.Parameters.AddWithValue("@family_id", Id);
                command.Parameters.AddWithValue("@type", type);
                command.Parameters.AddWithValue("@end_time", endTime);
                command.ExecuteNonQuery();
                command.Parameters.Clear();
            }
        }
    }

    public void ConfirmSave() => _removedMembers.Clear();
}

public class FamilyMember : PacketMarshaler
{
    public Character Character { get; set; }

    public uint Id { get; set; }
    public string Name { get; set; }
    public byte Level { get; set; }
    public byte HeirLevel { get; set; }
    public byte Role { get; set; }
    public bool Online => Character != null;
    public string Title { get; set; }
    public long RoleUpdateTime { get; set; }
    public long LoginRewardTime { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)Id);
        stream.Write(Name);
        stream.Write(Level);
        stream.Write(HeirLevel);
        stream.Write(Role);
        stream.Write(Online);
        stream.Write(Title);
        stream.Write(RoleUpdateTime);
        return stream;
    }
}
