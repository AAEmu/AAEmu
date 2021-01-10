using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpertExpandedPacket : GamePacket
    {
        private readonly byte _next;

        public SCExpertExpandedPacket(byte next) : base(SCOffsets.SCExpertExpandedPacket, 5)
        {
            _next = next;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_next);
            return stream;
        }
    }
}
