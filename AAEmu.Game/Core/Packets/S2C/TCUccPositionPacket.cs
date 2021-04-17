using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccPositionPacket : StreamPacket
    {
        public TCUccPositionPacket() : base(TCOffsets.TCUccPositionPacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((long) 0); // type
            stream.Write((long) 0); // x
            stream.Write((long) 0); // y
            stream.Write((float) 0); // z
            stream.Write((ulong) 0); // modified

            return stream;
        }
    }
}
