using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccCharNamePacket : StreamPacket
    {
        public TCUccCharNamePacket() : base(0x08)
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