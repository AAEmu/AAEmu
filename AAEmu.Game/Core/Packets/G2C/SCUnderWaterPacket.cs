using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnderWaterPacket : GamePacket
    {
        private readonly bool _start;

        public SCUnderWaterPacket(bool start) : base(SCOffsets.SCUnderWaterPacket, 5)
        {
            _start = start;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_start);
            return stream;
        }
    }
}
