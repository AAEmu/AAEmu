using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G;

public class LGRegisterGameServerPacket(GSRegisterResult result) : InternalPacket(LGOffsets.LGRegisterGameServerPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)result);
        return stream;
    }
}
