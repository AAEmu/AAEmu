using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACChallenge2Packet : LoginPacket
    {
        public ACChallenge2Packet() : base(LCOffsets.ACChallenge2Packet)
        {

        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(5000); // round
            stream.Write("xnDekI2enmWuAvwL"); // salt; length 16?
            stream.Write(new byte[32]); // hc

            return stream;
        }
    }
}
