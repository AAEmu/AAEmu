using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACAccountWarnedPacket : LoginPacket
    {
        public ACAccountWarnedPacket() : base(LCOffsets.ACAccountWarnedPacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) 0); // source
            stream.Write(""); // msg

            return stream;
        }
    }
}
