using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCActabilityPacket : GamePacket
    {
        public SCActabilityPacket() : base(0x130, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(true); // last
            stream.Write((byte) 34); // count
            for (var i = 0; i < 34; i++) // max count 100
            {
                stream.Write(i + 1); // action
                stream.Write(i); // point
                stream.Write((byte) 0); // step
            }

            return stream;
        }
    }
}