using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBmPointPacket : GamePacket
    {
        private readonly long _bmPoint;

        public SCBmPointPacket(long bmPoint) : base(SCOffsets.SCBmPointPacket, 1)
        {
            _bmPoint = bmPoint;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_bmPoint);
            return stream;
        }
    }
}
