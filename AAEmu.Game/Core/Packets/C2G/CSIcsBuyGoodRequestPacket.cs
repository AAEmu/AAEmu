using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSBuyGoodRequestPacket : GamePacket
    {
        public CSICSBuyGoodRequestPacket() : base(CSOffsets.CSIcsBuyGoodRequestPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSICSBuyGoodRequestPacket");
        }
    }
}
