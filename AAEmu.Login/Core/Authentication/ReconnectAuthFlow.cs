using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Wraps reconnection validation as an authentication flow so it goes through
/// <see cref="ILoginSession.AuthenticateAsync"/> like all other auth paths.
/// </summary>
public class ReconnectAuthFlow(ILoginController loginController, GameServerId gsId, AccountId accountId, uint token)
    : IAuthenticationFlow
{
    public async Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken)
    {
        var result = await loginController.Reconnect(gsId, accountId, token);
        return result.Success
            ? new AuthFlowResult.Success(result.AccountId)
            : new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
    }
}
