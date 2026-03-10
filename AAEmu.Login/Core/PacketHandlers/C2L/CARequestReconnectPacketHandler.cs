using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestReconnectPacket"/> which is sent by the client to request reconnection to a game
/// server.
/// </summary>
public class CARequestReconnectPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestReconnectPacket>
{
    public async Task Execute(CARequestReconnectPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var flow = new ReconnectAuthFlow(loginController, packet.GsId, packet.AccountId, packet.Cookie);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
