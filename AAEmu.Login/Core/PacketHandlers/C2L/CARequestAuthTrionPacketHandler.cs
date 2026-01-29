using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

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
        var passwordBytes = Helpers.StringToByteArray(packet.Password!);
        var passwordBase64 = Convert.ToBase64String(passwordBytes);
        var flow = new PasswordAuthFlow(loginController, packet.Username!, passwordBase64, session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
