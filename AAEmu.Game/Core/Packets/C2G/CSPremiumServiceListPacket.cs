using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPremiumServiceListPacket : GamePacket
    {
        public CSPremiumServiceListPacket() : base(CSOffsets.CSPremiumServiceListPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct

            _log.Warn("PremiumServiceList");

            var detail = new PremiumDetail();
            detail.CId = 8000001;
            detail.CName = "Премиум-подпиcка (30 дней)";
            detail.PId = 1;
            detail.PTime = 720;
            detail.Price = 300;

            Connection.SendPacket(new SCPremiumServiceListPacket(true, 1, detail, 0));
        }
    }
}
