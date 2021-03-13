using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTimeOfDayPacket : GamePacket
    {
        private readonly ushort _time;

        public SCTimeOfDayPacket(float time) : base(SCOffsets.SCTimeOfDayPacket, 5)
        {
            _time = (ushort)time;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_time);

            return stream;
        }
    }
}
