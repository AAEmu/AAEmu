using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.TowerDefs
{
    public class TowerDefKey : PacketMarshaler
    {
        public uint TowerDefId { get; set; }
        public ushort ZoneGroupId { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(TowerDefId);
            stream.Write(ZoneGroupId);
            return stream;
        }
    }
}
