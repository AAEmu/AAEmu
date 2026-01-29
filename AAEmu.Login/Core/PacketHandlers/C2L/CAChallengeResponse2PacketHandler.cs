using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAChallengeResponse2Packet"/> which is sent by the client in response to a challenge made by
/// the login server.
/// </summary>
/// <seealso cref="ACChallenge2Packet"/>
public class CAChallengeResponse2PacketHandler : ILoginPacketHandler<CAChallengeResponse2Packet>
{
    public async Task Execute(CAChallengeResponse2Packet packet, ILoginConnection connection,
        CancellationToken cancellationToken)
    {
        // Deny as this auth method is not supported
        await connection.SendPacketAsync(new ACLoginDeniedPacket(2), cancellationToken);
    }
}
