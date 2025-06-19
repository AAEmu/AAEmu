using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.L2G;

public class LGPlayerReconnectPacket(uint token) : InternalPacket(LGOffsets.LGPlayerReconnectPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(token);
        return stream;
    }
}
