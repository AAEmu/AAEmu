using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSetBreathPacket : GamePacket
    {
        private uint _timeLeft;
        
        public SCSetBreathPacket(uint timeLeft) : base(SCOffsets.SCSetBreathPacket, 1)
        {
            _timeLeft = timeLeft;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_timeLeft);
            return stream;
        }
    }
}
