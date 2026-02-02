using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthPacket"/> which is sent by the client to request authentication.
/// </summary>
public class CARequestAuthPacketHandler(ILoginController loginController, IOptions<KoreaAuthOptions> options)
    : ILoginPacketHandler<CARequestAuthPacket>
{
    public async Task Execute(CARequestAuthPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var flow = new KoreaAuthFlow(loginController, options, packet.Account!, session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
