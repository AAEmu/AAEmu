using AAEmu.Login.Core.Network.Connections;


namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow that uses challenge-response for password authentication.
/// </summary>
/// <remarks>
/// The flow sends a challenge packet in <see cref="IAuthenticationFlow.StartAsync"/>,
/// and the client responds with the password which is submitted via <see cref="ContinueAsync"/>.
/// </remarks>
public interface IChallengeAuthFlow : IAuthenticationFlow
{
    /// <summary>
    /// Continues the authentication flow with the password provided by the client.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="password">The password from the client's challenge response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of password verification.</returns>
    Task<AuthFlowResult> ContinueAsync(ILoginClient client, string password, CancellationToken cancellationToken);
}
