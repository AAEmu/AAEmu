using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class ExpeditionRecruitment : PacketMarshaler
{
    public FactionsEnum ExpeditionId { get; set; }
    public string Name { get; set; }
    public uint Level { get; set; }
    public string OwnerName { get; set; }
    public string Introduce { get; set; }
    public DateTime RegTime { get; set; }
    public DateTime EndTime { get; set; }
    public ushort Interest { get; set; }

    private int _memberCount;
    public int MemberCount
    {
        get
        {
            var expedition = ExpeditionManager.Instance.GetExpedition(ExpeditionId);
            if (expedition != null)
            {
                _memberCount = expedition.Members.Count;
            }

            return _memberCount;
        }
        set { _memberCount = value; }
    }
    public bool Apply { get; set; }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Connection = connection;
        command.Transaction = transaction;

        command.CommandText = "REPLACE INTO expedition_recruitments(`expedition_id`,`name`,`level`,`owner_name`,`introduce`,`reg_time`,`end_time`,`interest`, `member_count`, `apply`)" +
                                                           "VALUES (@expedition_id, @name, @level, @owner_name, @introduce, @reg_time, @end_time, @interest,  @member_count,  @apply)";
        command.Parameters.AddWithValue("@expedition_id", (uint)ExpeditionId);
        command.Parameters.AddWithValue("@name", Name);
        command.Parameters.AddWithValue("@level", Level);
        command.Parameters.AddWithValue("@owner_name", OwnerName);
        command.Parameters.AddWithValue("@introduce", Introduce);
        command.Parameters.AddWithValue("@reg_time", RegTime);
        command.Parameters.AddWithValue("@end_time", EndTime);
        command.Parameters.AddWithValue("@interest", Interest);
        command.Parameters.AddWithValue("@member_count", MemberCount);
        command.Parameters.AddWithValue("@apply", Apply);
        command.ExecuteNonQuery();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)ExpeditionId);
        stream.Write(Name);
        stream.Write(Level);
        stream.Write(OwnerName);
        stream.Write(Introduce);
        stream.Write(RegTime);
        stream.Write(EndTime);
        stream.Write(Interest);
        stream.Write(MemberCount);
        stream.Write(Apply);
        return stream;
    }
}
