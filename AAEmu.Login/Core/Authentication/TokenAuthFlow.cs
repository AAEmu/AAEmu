using System.Net;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Token-trusted authentication flow for web/launcher (passport) auth.
/// Single-step: authenticates the account by username without a password — the launcher
/// session token is trusted upstream. The account is created on first use.
/// </summary>
public class TokenAuthFlow(ILoginController loginController, string username, IPAddress clientIp)
    : IAuthenticationFlow
{
    public async Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken)
    {
        var result = await loginController.LoginTrusted(username, clientIp, cancellationToken);

        return result.Success
            ? new AuthFlowResult.Success(result.AccountId, username)
            : new AuthFlowResult.Denied(result.DenialReason);
    }
}
