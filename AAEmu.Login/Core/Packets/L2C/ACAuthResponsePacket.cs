using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACAuthResponsePacket : LoginPacket
    {
        private readonly uint _accountId;
        private readonly byte[] _wsk;
        private readonly byte _slotCount;

        public ACAuthResponsePacket(uint accountId, byte slotCount) : base(LCOffsets.ACAuthResponsePacket)
        {
            _accountId = accountId;
            _wsk = new byte[32];
            _slotCount = slotCount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_accountId);
            stream.Write(_wsk, true);
            stream.Write(_slotCount);

            return stream;
        }
    }
}
