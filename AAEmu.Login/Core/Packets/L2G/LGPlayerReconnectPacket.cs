using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.Packets.L2G;

/// <summary>
/// A packet sent by the login server to the game server in response to a request for player reconnection.
/// </summary>
/// <param name="token"></param>
/// <seealso cref="GLPlayerReconnectPacket"/>
public class LGPlayerReconnectPacket(uint token) : InternalPacket(LGOffsets.LGPlayerReconnectPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(token);
        return stream;
    }
}
