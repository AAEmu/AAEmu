using AAEmu.Commons.Network;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class ExpeditionRolePolicy : PacketMarshaler
{
    public FactionsEnum ExpeditionId { get; set; }
    public byte Role { get; set; }
    public string Name { get; set; } // TODO max length 128
    public bool DominionDeclare { get; set; }
    public bool Invite { get; set; }
    public bool Expel { get; set; }
    public bool Promote { get; set; }
    public bool Dismiss { get; set; }
    public bool Chat { get; set; }
    public bool ManagerChat { get; set; }
    public bool SiegeMaster { get; set; }
    public bool JoinSiege { get; set; }
    /// <summary>
    /// The client's role-policy struct has 10 boolean permission flags, not 9. Missing this field makes
    /// every policy entry 1 byte short on the wire, which desyncs the client's parser for everything
    /// sent right after SCExpeditionRolePolicyListPacket (a fixed-size array, not length-delimited).
    /// </summary>
    public bool UseInstance { get; set; }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO " +
                "expedition_role_policies(`expedition_id`,`role`,`name`,`dominion_declare`,`invite`,`expel`,`promote`,`dismiss`, `chat`, `manager_chat`, `siege_master`, `join_siege`, `use_instance`) " +
                "VALUES (@expedition_id,@role,@name,@dominion_declare,@invite,@expel,@promote,@dismiss,@chat,@manager_chat,@siege_master,@join_siege,@use_instance)";

            command.Parameters.AddWithValue("@expedition_id", this.ExpeditionId);
            command.Parameters.AddWithValue("@role", this.Role);
            command.Parameters.AddWithValue("@name", this.Name);
            command.Parameters.AddWithValue("@dominion_declare", this.DominionDeclare);
            command.Parameters.AddWithValue("@invite", this.Invite);
            command.Parameters.AddWithValue("@expel", this.Expel);
            command.Parameters.AddWithValue("@promote", this.Promote);
            command.Parameters.AddWithValue("@dismiss", this.Dismiss);
            command.Parameters.AddWithValue("@chat", this.Chat);
            command.Parameters.AddWithValue("@manager_chat", this.ManagerChat);
            command.Parameters.AddWithValue("@siege_master", this.SiegeMaster);
            command.Parameters.AddWithValue("@join_siege", this.JoinSiege);
            command.Parameters.AddWithValue("@use_instance", this.UseInstance);
            command.ExecuteNonQuery();
        }
    }

    public override void Read(PacketStream stream)
    {
        ExpeditionId = (FactionsEnum)stream.ReadUInt32();
        Role = stream.ReadByte();
        Name = stream.ReadString();
        DominionDeclare = stream.ReadBoolean();
        Invite = stream.ReadBoolean();
        Expel = stream.ReadBoolean();
        Promote = stream.ReadBoolean();
        Dismiss = stream.ReadBoolean();
        Chat = stream.ReadBoolean();
        ManagerChat = stream.ReadBoolean();
        SiegeMaster = stream.ReadBoolean();
        JoinSiege = stream.ReadBoolean();
        UseInstance = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)ExpeditionId);
        stream.Write(Role);
        stream.Write(Name);
        stream.Write(DominionDeclare);
        stream.Write(Invite);
        stream.Write(Expel);
        stream.Write(Promote);
        stream.Write(Dismiss);
        stream.Write(Chat);
        stream.Write(ManagerChat);
        stream.Write(SiegeMaster);
        stream.Write(JoinSiege);
        stream.Write(UseInstance);
        return stream;
    }

    public ExpeditionRolePolicy Clone()
    {
        var rolePolicy = new ExpeditionRolePolicy
        {
            ExpeditionId = ExpeditionId, Role = Role, Name = Name, DominionDeclare = DominionDeclare,
            Invite = Invite,
            Expel = Expel,
            Promote = Promote,
            Dismiss = Dismiss,
            Chat = Chat,
            ManagerChat = ManagerChat,
            SiegeMaster = SiegeMaster,
            JoinSiege = JoinSiege,
            UseInstance = UseInstance
        };
        return rolePolicy;
    }
}
