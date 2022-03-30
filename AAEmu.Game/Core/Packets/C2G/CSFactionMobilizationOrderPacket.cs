using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionMobilizationOrderPacket : GamePacket
    {
        public CSFactionMobilizationOrderPacket() : base(CSOffsets.CSFactionMobilizationOrderPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFactionMobilizationOrderPacket");
        }
    }
}
