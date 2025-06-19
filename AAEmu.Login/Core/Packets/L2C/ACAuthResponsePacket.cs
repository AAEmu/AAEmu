using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C;

public class ACAuthResponsePacket(AccountId accountId, byte slotCount) : LoginPacket(LCOffsets.ACAuthResponsePacket)
{
    private readonly byte[] _wsk = new byte[32];

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(accountId.Value);
        stream.Write(_wsk, true);
        stream.Write(slotCount);

        return stream;
    }
}
