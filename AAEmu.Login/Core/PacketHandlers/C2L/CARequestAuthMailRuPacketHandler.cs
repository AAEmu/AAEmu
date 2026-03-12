using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Services;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthMailRuPacket"/> which is sent by the client to request authentication via
/// Mail.Ru.
/// </summary>
public class CARequestAuthMailRuPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthMailRuPacket>
{
    public async Task Execute(CARequestAuthMailRuPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var tokenHex = Convert.ToHexString(packet.Token!);
        var flow = new PasswordAuthFlow(loginController, packet.Id!, Password.FromSha256Hex(tokenHex), session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
