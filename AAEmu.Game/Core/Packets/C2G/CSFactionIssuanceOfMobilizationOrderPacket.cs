using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionIssuanceOfMobilizationOrderPacket : GamePacket
    {
        public CSFactionIssuanceOfMobilizationOrderPacket() : base(CSOffsets.CSFactionIssuanceOfMobilizationOrderPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFactionIssuanceOfMobilizationOrderPacket");
        }
    }
}
