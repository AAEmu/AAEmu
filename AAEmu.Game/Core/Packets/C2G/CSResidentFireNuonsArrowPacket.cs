using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResidentFireNuonsArrowPacket : GamePacket
    {
        public CSResidentFireNuonsArrowPacket() : base(CSOffsets.CSResidentFireNuonsArrowPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSResidentFireNuonsArrowPacket");
        }
    }
}
