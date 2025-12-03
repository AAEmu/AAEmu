using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInstantGameRemainTimePacket : GamePacket
    {
        private uint _remainTime;

        // TODO: Check offset!!
        public SCInstantGameRemainTimePacket(uint remainTime)
            : base(SCOffsets.SCInstantGameUnkPacket, 1)
        {
            _remainTime = remainTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_remainTime);
            return stream;
        }
    }
}
