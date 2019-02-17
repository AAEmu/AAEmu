using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTradeOkPacket : GamePacket
    {
        public CSTradeOkPacket() : base(0x0f4, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("TradeOk");
        }
    }
}
