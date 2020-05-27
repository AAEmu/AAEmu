using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelTradePacket : GamePacket
    {
        public CSCancelTradePacket() : base(0x0ef, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var reason = stream.ReadInt32();
            
            _log.Warn("CancelTrade, Reason: {0}", reason);
            TradeManager.Instance.CancelTrade(DbLoggerCategory.Database.Connection.ActiveChar.ObjId, reason);
        }
    }
}
