using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G;

/// <summary>
/// A packet sent by the login server to the game server to request the list of characters that an account has.
/// </summary>
/// <param name="connectionId">The identifier of the connection to the client.</param>
/// <param name="requestId">The identifier of the request.</param>
/// <param name="accountId">The identifier of the account.</param>
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
