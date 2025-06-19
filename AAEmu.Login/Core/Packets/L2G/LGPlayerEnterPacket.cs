using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G;

public class LGPlayerEnterPacket(AccountId accountId, ConnectionId connectionId) : InternalPacket(LGOffsets.LGPlayerEnterPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(accountId.Value);
        stream.Write(connectionId.Value);
        return stream;
    }
}
