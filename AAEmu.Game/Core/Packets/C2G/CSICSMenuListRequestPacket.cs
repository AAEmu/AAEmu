using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSMenuListRequestPacket : GamePacket
    {
        public CSICSMenuListRequestPacket() : base(CSOffsets.CSICSMenuListRequestPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("CSICSMenuListRequestPacket");
            
            Connection.SendPacket(new SCICSMenuListPacket(1));
            Connection.SendPacket(new SCICSExchangeRatioPacket(0));
            Connection.SendPacket(new SCICSCashPointPacket(0, 0, false));
        }
    }
}
