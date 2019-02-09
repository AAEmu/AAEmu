using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelTradePacket : GamePacket
    {
        public CSCancelTradePacket() : base(0x0ec, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var reason = stream.ReadInt32();
            
            _log.Warn("CancelTrade, Reason: {0}", reason);
        }
    }
}
