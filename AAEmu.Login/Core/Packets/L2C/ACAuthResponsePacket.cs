using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

public class ACAuthResponsePacket : LoginPacket
{
    private readonly ulong _accountId;
    private readonly string _wsk;
    private readonly byte _slotCount;
    private readonly string _encKey;

    public ACAuthResponsePacket(ulong accountId, byte slotCount) : base(LCOffsets.ACAuthResponsePacket)
    {
        _accountId = accountId;
        _wsk = "5b2c4bb76c8a404da3d5c9c97ddd7dc6"; //TODO: генерация //ADBDAE13A28D415889FE34F20B268C97
        _slotCount = slotCount;
        _encKey = ""; //TODO: генерация
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_accountId);  // accountId
        stream.Write(_wsk);        // wsk, 32 bytes
        stream.Write(_slotCount);  // slotCount
        stream.Write(_encKey);     // encKey add for 5.7.5.0, 127 bytes

        return stream;
    }
}
