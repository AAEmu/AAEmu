using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

public class ACEnterWorldDeniedPacket(byte reason) : LoginPacket(LCOffsets.ACEnterWorldDeniedPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(reason);

        return stream;
    }
}
