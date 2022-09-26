using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACEnterOtpPacket : LoginPacket
    {
        public ACEnterOtpPacket() : base(LCOffsets.ACEnterOtpPacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(0); // mt
            stream.Write(0); // ct

            return stream;
        }
    }
}
