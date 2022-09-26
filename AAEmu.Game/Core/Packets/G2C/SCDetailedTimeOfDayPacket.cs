using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDetailedTimeOfDayPacket : GamePacket
    {
        private readonly float _time;
        private readonly float _speed;
        private readonly float _start;
        private readonly float _end;

        public SCDetailedTimeOfDayPacket(float time) : base(SCOffsets.SCDetailedTimeOfDayPacket, 5)
        {
            _time = time;
            _speed = 0.0016666f;
            _start = 0f;
            _end = 24f;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_time);
            stream.Write(_speed);
            stream.Write(_start);
            stream.Write(_end);
            return stream;
        }
    }
}
