using AAEmu.Commons.Network;

namespace AAEmu.Commons.Models
{
    public class LoginCharacterInfo : PacketMarshaler
    {
        public uint Id { get; set; }
        public ulong AccountId { get; set; }
        public byte GsId { get; set; }
        public string Name { get; set; }
        public byte Race { get; set; }
        public byte Gender { get; set; }

        public override void Read(PacketStream stream)
        {
            Id = stream.ReadUInt32();
            AccountId = stream.ReadUInt32();
            Name = stream.ReadString();
            Race = stream.ReadByte();
            Gender = stream.ReadByte();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(AccountId);
            stream.Write(Name);
            stream.Write(Race);
            stream.Write(Gender);
            return stream;
        }
    }
}
