using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccComplexPacket : StreamPacket
    {
        public TCUccComplexPacket() : base(0x05)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 4; i++)
                stream.Write((long) 0); // type
            stream.Write((ulong) 0); // modified

            return stream;
        }
    }
}