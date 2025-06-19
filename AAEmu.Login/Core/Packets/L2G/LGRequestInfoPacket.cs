using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G;

public class LGRequestInfoPacket(ConnectionId connectionId, uint requestId, AccountId accountId)
    : InternalPacket(LGOffsets.LGRequestInfoPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(connectionId.Value);
        stream.Write(requestId);
        stream.Write((ulong)accountId.Value);
        return stream;
    }
}
