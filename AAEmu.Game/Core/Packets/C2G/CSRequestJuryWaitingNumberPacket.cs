using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestJuryWaitingNumberPacket : GamePacket
    {
        public CSRequestJuryWaitingNumberPacket() : base(0x077, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("RequestJuryWaitingNumber");
        }
    }
}
