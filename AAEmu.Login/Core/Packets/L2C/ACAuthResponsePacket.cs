using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACAuthResponsePacket : LoginPacket
    {
        private readonly uint _accountId;
        private readonly byte[] _wsk;

        public ACAuthResponsePacket(uint accountId) : base(0x03)
        {
            _accountId = accountId;
            _wsk = new byte[32];
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_accountId);
            stream.Write(_wsk, true);

            return stream;
        }
    }
}