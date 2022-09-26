using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCJuryWaitStatusPacket : GamePacket
    {
        private readonly int _count;
        private readonly int _total;
        private readonly uint _sentnce;
        public SCJuryWaitStatusPacket(int count, int total, uint sentence) : base(SCOffsets.SCJuryWaitStatusPacket, 5)
        {
            _count = count;
            _total = total;
            _sentnce = sentence;

        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_count);
            stream.Write(_total);
            stream.Write(_sentnce);
            return stream;
        }
    }
}
