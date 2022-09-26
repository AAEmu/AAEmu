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

        public ACAuthResponsePacket(ulong accountId, byte slotCount) : base(LCOffsets.ACAuthResponsePacket)
        {
            _accountId = accountId;
            //_wsk = new byte[32]; 
            _wsk = "65CCBF5AF8DB8B633D3C03C5A8735601"; //TODO: генерация //ADBDAE13A28D415889FE34F20B268C97
            _slotCount = slotCount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_accountId);
            stream.Write(_wsk, true);
            stream.Write(_slotCount);
            //stream.Write((short)0); //add for 5.1

            return stream;
        }
    }
}
