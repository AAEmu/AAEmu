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
    public async Task Execute(CAChallengeResponsePacket packet, LoginConnection connection)
    {
        // Deny as this auth method is not supported
        await connection.SendPacketAsync(new ACLoginDeniedPacket(3), CancellationToken.None);
    }
}
