using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestJuryWaitingNumberPacket : GamePacket
    {
        public CSRequestJuryWaitingNumberPacket() : base(CSOffsets.CSRequestJuryWaitingNumberPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("RequestJuryWaitingNumber");
        }
    }
}
