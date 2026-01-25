using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G;

/// <summary>
/// A packet sent by the login server to the game server to request that a player be allowed to enter the game.
/// </summary>
/// <param name="accountId">The account identifier.</param>
/// <param name="connectionId">The unique identifier of the connection to the client.</param>
public class LGPlayerEnterPacket(AccountId accountId, ConnectionId connectionId)
    : InternalPacket(LGOffsets.LGPlayerEnterPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(accountId.Value);
        stream.Write(connectionId.Value);
        return stream;
    }
}
