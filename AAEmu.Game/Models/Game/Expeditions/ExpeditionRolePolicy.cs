using AAEmu.Commons.Network;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions
{
    public class ExpeditionRolePolicy : PacketMarshaler
    {
        public uint Id { get; set; }
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

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "REPLACE INTO " +
                    "expedition_role_policies(`expedition_id`,`role`,`name`,`dominion_declare`,`invite`,`expel`,`promote`,`dismiss`, `chat`, `manager_chat`, `siege_master`, `join_siege`) " +
                    "VALUES (@expedition_id,@role,@name,@dominion_declare,@invite,@expel,@promote,@dismiss,@chat,@manager_chat,@siege_master,@join_siege)";

                command.Parameters.AddWithValue("@expedition_id", this.Id);
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
                command.ExecuteNonQuery();
            }
        }

        public override void Read(PacketStream stream)
        {
            Id = stream.ReadUInt32();
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
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
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
            return stream;
        }

        public ExpeditionRolePolicy Clone()
        {
            var rolePolicy = new ExpeditionRolePolicy();
            rolePolicy.Id = Id;
            rolePolicy.Role = Role;
            rolePolicy.Name = Name;
            rolePolicy.DominionDeclare = DominionDeclare;
            rolePolicy.Invite = Invite;
            rolePolicy.Expel = Expel;
            rolePolicy.Promote = Promote;
            rolePolicy.Dismiss = Dismiss;
            rolePolicy.Chat = Chat;
            rolePolicy.ManagerChat = ManagerChat;
            rolePolicy.SiegeMaster = SiegeMaster;
            rolePolicy.JoinSiege = JoinSiege;
            return rolePolicy;
        }
    }
}
