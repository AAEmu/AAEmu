using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACAccountWarnedPacket : LoginPacket
    {
        private readonly byte _source;
        private readonly string _msg;
        public ACAccountWarnedPacket(byte source, string msg) : base(0x0D)
        {
            _source = source;
            _msg = msg;
        }
        public ACAccountWarnedPacket() : base(0x0D)
        {
            _source = 0;
            _msg = "";
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_source); // source
            stream.Write(_msg);    // msg

            return stream;
        }
    }
}
