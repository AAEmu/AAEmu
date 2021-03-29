using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACAuthResponsePacket : LoginPacket
    {
        private readonly ulong _accountId;
        //private readonly byte[] _wsk;
        private readonly string _wsk;
        private readonly byte _slotCount;
        private readonly string _encKey;

        public ACAuthResponsePacket(ulong accountId, byte slotCount) : base(0x03)
        {
            _accountId = accountId;
            //_wsk = new byte[32]; 
            _wsk = ""; // в 7039 пусто TODO: генерация //65CCBF5AF8DB8B633D3C03C5A8735601
            _slotCount = slotCount;
            _encKey = ""; //TODO: генерация
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_accountId);  // accountId
            stream.Write(_wsk);        // wsk
            stream.Write(_slotCount);  // slotCount
            stream.Write(_encKey);     // encKey add for 5.7.5.0
            stream.Write(0); // unk

            return stream;
        }
    }
}
