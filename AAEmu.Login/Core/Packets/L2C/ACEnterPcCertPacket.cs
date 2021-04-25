using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACEnterPcCertPacket : LoginPacket
    {
        public ACEnterPcCertPacket() : base(LCOffsets.ACEnterPcCertPacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((int) 0); // mt
            stream.Write((int) 0); // ct

            return stream;
        }
    }
}
