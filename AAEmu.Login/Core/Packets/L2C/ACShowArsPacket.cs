using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACShowArsPacket : LoginPacket
    {
        public ACShowArsPacket() : base(0x06)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(""); // num
            stream.Write((uint) 0); // timeout

            return stream;
        }
    }
}