using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnknownInstancePacket : GamePacket
    {
        public CSUnknownInstancePacket() : base(CSOffsets.CSUnknownInstancePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt16();
            var zoneId = stream.ReadInt32();
            var instId = stream.ReadUInt32();
        }
    }
}