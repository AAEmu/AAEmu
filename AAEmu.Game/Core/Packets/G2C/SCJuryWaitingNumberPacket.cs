using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCJuryWaitingNumberPacket : GamePacket
    {
        private readonly int _waitingNumber;

        public SCJuryWaitingNumberPacket(int waitingNumber) : base(SCOffsets.SCJuryWaitingNumberPacket, 5)
        {
            _waitingNumber = waitingNumber;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_waitingNumber);
            return stream;
        }
    }
}
