using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Services;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthPacket_0x004"/> XML authentication packet.
/// </summary>
public class CARequestAuth_004_PacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthPacket_0x004>
{
    public async Task Execute(CARequestAuthPacket_0x004 packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packet.Username) || string.IsNullOrWhiteSpace(packet.Password))
            return;

        var flow = new PasswordAuthFlow(loginController, packet.Username,
            Password.FromSha256Hex(packet.Password), session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
