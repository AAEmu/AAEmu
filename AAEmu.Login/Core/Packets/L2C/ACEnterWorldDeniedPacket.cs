using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACEnterWorldDeniedPacket : LoginPacket
    {
        public ACEnterWorldDeniedPacket() : base(0x0B)
        {
            
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) 0); // reason
            
            return stream;
        }
    }
}