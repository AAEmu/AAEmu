using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.G2L;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G;

/// <summary>
/// A packet sent by the login server to the game server in response to a request to register a game server.
/// </summary>
/// <param name="result">The result of the request.</param>
/// <seealso cref="GLRegisterGameServerPacket"/>
public class LGRegisterGameServerPacket(GSRegisterResult result) : InternalPacket(LGOffsets.LGRegisterGameServerPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)result);
        return stream;
    }
}
