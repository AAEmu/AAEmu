using System;

using AAEmu.Commons.Network;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class Applicant : PacketMarshaler
{
    public uint ExpeditionId { get; set; }
    public string Memo { get; set; }
    public uint CharacterId { get; set; }
    public string CharacterName { get; set; }
    public byte CharacterLevel { get; set; }
    public DateTime RegTime { get; set; }

    public Applicant()
    {
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ExpeditionId, CharacterId);
    }

    public override bool Equals(object obj)
    {
        if (obj is Applicant other)
        {
            return ExpeditionId == other.ExpeditionId && CharacterId == other.CharacterId;
        }
        return false;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ExpeditionId);
        stream.Write(Memo);
        return stream;
    }

    public PacketStream WriteInfo(PacketStream stream)
    {
        stream.Write(CharacterId);
        stream.Write(CharacterName);
        stream.Write(CharacterLevel);
        stream.Write(ExpeditionId);
        stream.Write(Memo);
        stream.Write(RegTime);
        return stream;
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Connection = connection;
        command.Transaction = transaction;

        command.CommandText = "REPLACE INTO expedition_applicants(`expedition_id`,`character_id`,`character_name`,`character_level`,`memo`,`reg_time`)" +
                              "VALUES (@expedition_id, @character_id, @character_name, @character_level, @memo, @reg_time)";
        command.Parameters.AddWithValue("@expedition_id", this.ExpeditionId);
        command.Parameters.AddWithValue("@character_id", this.CharacterId);
        command.Parameters.AddWithValue("@character_name", this.CharacterName);
        command.Parameters.AddWithValue("@character_level", this.CharacterLevel);
        command.Parameters.AddWithValue("@memo", this.Memo);
        command.Parameters.AddWithValue("@reg_time", this.RegTime);
        command.ExecuteNonQuery();
    }
}
