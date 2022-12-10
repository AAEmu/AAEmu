using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPremiumServiceBuyPacket : GamePacket
    {
        public CSPremiumServiceBuyPacket() : base(CSOffsets.CSPremiumServiceBuyPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var cid = stream.ReadInt32();
            
            _log.Warn("PremiumServiceBuy, CId: {0}", cid);
        }
    }
}
