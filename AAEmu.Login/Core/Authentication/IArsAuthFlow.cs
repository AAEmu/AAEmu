using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow that requires ARS (Automatic Response System/자동응답시스템) verification.
/// </summary>
/// <remarks>
/// ARS is automated phone verification used in Korean authentication. The system calls the user's
/// registered phone number, and they enter a code displayed on screen via IVR (Interactive Voice Response).
/// Since Korean phone numbers are tied to real identity, this serves as identity verification.
/// <para/>
/// Unlike OTP and certificate flows, ARS is callback-based rather than packet-based.
/// The external phone system calls back to the server to confirm success/failure.
/// <para/>
/// Returns <see cref="AuthFlowResult"/> values; <see cref="ILoginSession"/> manages the lifecycle.
/// </remarks>
public interface IArsAuthFlow : IAuthenticationFlow
{
    /// <summary>
    /// Completes the ARS verification with the result from the external phone system callback.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="success">Whether the user successfully entered the code via phone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of ARS verification.</returns>
    Task<AuthFlowResult> CompleteArsAsync(ILoginClient client, bool success, CancellationToken cancellationToken);
}
