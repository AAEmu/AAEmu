using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Team
{
    public class LootingRule : PacketMarshaler
    {
        public byte LootMethod { get; set; }
        public byte Type { get; set; }
        public uint Id { get; set; }
        public bool RollForBop { get; set; }

        public LootingRule()
        {
            // TODO - MAKE IT CONFIGURABLE (config.json)
            LootMethod = 1;
            Type = 2;
            Id = 0;
            RollForBop = true;
        }
        
        public override void Read(PacketStream stream)
        {
            LootMethod = stream.ReadByte();
            Type = stream.ReadByte();
            Id = stream.ReadUInt32();
            RollForBop = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(LootMethod);
            stream.Write(Type);
            stream.Write(Id);
            stream.Write(RollForBop);
            return stream;
        }
    }
}
