using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.DB.Game;
using AAEmu.Game.Utils.DB;

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

        public void Save(GameDBContext ctx)
        {
            ctx.ExpeditionRolePolicies.RemoveRange(
                ctx.ExpeditionRolePolicies.Where(p => p.ExpeditionId == this.Id && p.Role == this.Role));
            ctx.SaveChanges();

            ctx.ExpeditionRolePolicies.Add(this.ToEntity());
            ctx.SaveChanges();
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

        public DB.Game.ExpeditionRolePolicies ToEntity()
            =>
            new DB.Game.ExpeditionRolePolicies()
            {
                ExpeditionId    = this.Id                                  ,
                Role            = this.Role                                ,
                Name            = this.Name                                ,
                DominionDeclare = this.DominionDeclare ? (byte)1 : (byte)0 ,
                Invite          = this.Invite          ? (byte)1 : (byte)0 ,
                Expel           = this.Expel           ? (byte)1 : (byte)0 ,
                Promote         = this.Promote         ? (byte)1 : (byte)0 ,
                Dismiss         = this.Dismiss         ? (byte)1 : (byte)0 ,
                Chat            = this.Chat            ? (byte)1 : (byte)0 ,
                ManagerChat     = this.ManagerChat     ? (byte)1 : (byte)0 ,
                SiegeMaster     = this.SiegeMaster     ? (byte)1 : (byte)0 ,
                JoinSiege       = this.JoinSiege       ? (byte)1 : (byte)0 ,
            };

        public static explicit operator ExpeditionRolePolicy(ExpeditionRolePolicies v) 
            =>
            new ExpeditionRolePolicy()
            {
                Id              = v.ExpeditionId        ,
                Role            = v.Role                ,
                Name            = v.Name                ,
                DominionDeclare = v.DominionDeclare == 1,
                Invite          = v.Invite          == 1,
                Expel           = v.Expel           == 1,
                Promote         = v.Promote         == 1,
                Dismiss         = v.Dismiss         == 1,
                Chat            = v.Chat            == 1,
                ManagerChat     = v.ManagerChat     == 1,
                SiegeMaster     = v.SiegeMaster     == 1,
                JoinSiege       = v.JoinSiege       == 1,
            };
    }
}
