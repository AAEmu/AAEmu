using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Services;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthTrionPacket"/> which is sent by the client to request authentication using
/// Trion credentials.
/// </summary>
public class CARequestAuthTrionPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthTrionPacket>
{
    public async Task Execute(CARequestAuthTrionPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var flow = new PasswordAuthFlow(loginController, packet.Username!,
            Password.FromSha256Hex(packet.Password!), session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
