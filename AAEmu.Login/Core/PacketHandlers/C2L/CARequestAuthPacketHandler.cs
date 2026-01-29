using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthPacket"/> which is sent by the client to request authentication.
/// </summary>
public class CARequestAuthPacketHandler(ILoginController loginController) : ILoginPacketHandler<CARequestAuthPacket>
{
    public async Task Execute(CARequestAuthPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var flow = new KrLoginFlow(loginController, packet.Account!);
        await session.AuthenticateAsync(flow, cancellationToken);
    }

    /// <summary>
    /// Simple auth flow wrapping the Kr method (username-only login).
    /// Will be replaced by KoreaAuthFlow with multi-step auth support.
    /// </summary>
    private class KrLoginFlow(ILoginController loginController, string username) : IAuthenticationFlow
    {
        public async Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken)
        {
            var result = await loginController.Login(username);
            return result.Success
                ? new AuthFlowResult.Success(result.AccountId, username)
                : new AuthFlowResult.Denied(result.DenialReason);
        }
    }
}
