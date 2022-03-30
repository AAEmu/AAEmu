using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetVisiblePremiumServicePacket : GamePacket
    {
        public CSSetVisiblePremiumServicePacket() : base(CSOffsets.CSSetVisiblePremiumServicePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSetVisiblePremiumServicePacket");
        }
    }
}
