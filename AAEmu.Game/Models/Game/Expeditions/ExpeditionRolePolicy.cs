using AAEmu.Commons.Network;

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
    }
}
