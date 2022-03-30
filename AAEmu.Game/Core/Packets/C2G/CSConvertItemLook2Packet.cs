using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConvertItemLook2Packet : GamePacket
    {
        public CSConvertItemLook2Packet() : base(CSOffsets.CSConvertItemLook2Packet, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSConvertItemLook2Packet");
        }
    }
}
