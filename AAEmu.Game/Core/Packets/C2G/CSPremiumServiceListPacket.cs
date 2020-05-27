using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CashShop;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPremiumServiceListPacket : GamePacket
    {
        public CSPremiumServiceListPacket() : base(0x136, 1)
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

            DbLoggerCategory.Database.Connection.SendPacket(new SCPremiumServiceListPacket(true, 1, detail, 0));
        }
    }
}
