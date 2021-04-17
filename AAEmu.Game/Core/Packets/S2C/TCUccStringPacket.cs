using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccStringPacket : StreamPacket
    {
        public TCUccStringPacket() : base(TCOffsets.TCUccStringPacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((long) 0); // type
            stream.Write((ushort) 0); // "data" length; old version max length 100
            stream.Write((ulong) 0); // modified

            return stream;
        }
    }
}
