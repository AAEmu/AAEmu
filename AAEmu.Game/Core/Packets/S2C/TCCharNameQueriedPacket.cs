using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCCharNameQueriedPacket : StreamPacket
    {
        public TCCharNameQueriedPacket() : base(0x09)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((uint) 0); // type
            stream.Write((ushort) 0); // "name" length

            return stream;
        }
    }
}