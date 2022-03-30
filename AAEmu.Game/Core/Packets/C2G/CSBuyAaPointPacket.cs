using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyAaPointPacket : GamePacket
    {
        public CSBuyAaPointPacket() : base(CSOffsets.CSBuyAaPointPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSBuyAaPointPacket");
        }
    }
}
