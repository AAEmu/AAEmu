using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACChallenge2Packet : LoginPacket
    {
        public ACChallenge2Packet() : base(0x04)
        {
            
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((int) 0); // round
            stream.Write(""); // salt; length 16?
            for (var i = 0; i < 8; i++)
                stream.Write((uint) 0); // hc
            
            return stream;
        }
    }
}