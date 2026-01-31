using System.Net;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Services;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow using username and password (EU method).
/// This is a single-step flow that completes immediately.
/// </summary>
public class PasswordAuthFlow(
    ILoginController loginController,
    string username,
    Password password,
    IPAddress clientIp) : IAuthenticationFlow
{
    public async Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken)
    {
        var result = await loginController.Login(username, password, clientIp, cancellationToken);

        return result.Success
            ? new AuthFlowResult.Success(result.AccountId, username)
            : new AuthFlowResult.Denied(result.DenialReason);
    }
}
