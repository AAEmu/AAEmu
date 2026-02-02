using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow that requires OTP (One-Time Password) verification.
/// </summary>
/// <remarks>
/// Returns <see cref="AuthFlowResult"/> values; <see cref="ILoginSession"/> manages the lifecycle.
/// </remarks>
public interface IOtpAuthFlow : IAuthenticationFlow
{
    /// <summary>
    /// Submits the OTP code provided by the client.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="otpCode">The OTP code entered by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of OTP verification.</returns>
    Task<AuthFlowResult> SubmitOtpAsync(ILoginClient client, string otpCode, CancellationToken cancellationToken);
}
