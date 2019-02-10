using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSMoneyRequestPacket : GamePacket
    {
        public CSICSMoneyRequestPacket() : base(0x11a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("ICSMoneyRequest");

            Connection.SendPacket(new SCICSCashPointPacket(5678));
        }
    }
}
