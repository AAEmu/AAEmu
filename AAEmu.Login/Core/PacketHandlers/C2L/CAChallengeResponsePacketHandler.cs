using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAChallengeResponsePacket"/> which is sent by the client in response to a challenge made by
/// the login server.
/// </summary>
/// <seealso cref="ACChallengePacket"/>
public class CAChallengeResponsePacketHandler : ILoginPacketHandler<CAChallengeResponsePacket>
{
    public void Execute(CAChallengeResponsePacket packet, LoginConnection connection)
    {
        // Deny as this auth method is not supported
        connection.SendPacket(new ACLoginDeniedPacket(3));
    }
}
