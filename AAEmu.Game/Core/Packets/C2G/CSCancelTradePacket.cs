using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelTradePacket : GamePacket
    {
        public CSCancelTradePacket() : base(CSOffsets.CSCancelTradePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var reason = stream.ReadInt32();
            
            _log.Warn("CancelTrade, Reason: {0}", reason);
            TradeManager.Instance.CancelTrade(Connection.ActiveChar.ObjId, reason);
        }
    }
}
