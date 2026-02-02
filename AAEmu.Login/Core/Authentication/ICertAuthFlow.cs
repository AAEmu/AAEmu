using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow that requires certificate verification.
/// </summary>
/// <remarks>
/// Returns <see cref="AuthFlowResult"/> values; <see cref="ILoginSession"/> manages the lifecycle.
/// </remarks>
public interface ICertAuthFlow : IAuthenticationFlow
{
    /// <summary>
    /// Submits the certificate number provided by the client.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="certNumber">The certificate number entered by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of certificate verification.</returns>
    Task<AuthFlowResult> SubmitCertAsync(ILoginClient client, string certNumber, CancellationToken cancellationToken);
}
