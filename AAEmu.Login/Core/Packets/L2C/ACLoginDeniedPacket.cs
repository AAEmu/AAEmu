using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACLoginDeniedPacket : LoginPacket
    {
        private byte _reason;

        public ACLoginDeniedPacket(byte reason) : base(0x0C)
        {
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(""); // vp
            stream.Write(""); // msg

            return stream;
        }
    }
}