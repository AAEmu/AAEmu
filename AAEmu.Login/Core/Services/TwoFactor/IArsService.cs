using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Service for ARS (Automatic Response System) phone callback verification.
/// </summary>
public interface IArsService
{
    /// <summary>
    /// Creates a new ARS verification session.
    /// </summary>
    /// <param name="accountId">The account ID to create a session for.</param>
    /// <param name="timeoutSeconds">How long the session is valid.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created session with the verification code.</returns>
    Task<ArsSession> CreateSessionAsync(AccountId accountId, int timeoutSeconds, CancellationToken cancellationToken);

    /// <summary>
    /// Completes an ARS session, called when the user confirms via phone.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="sessionCode">The session code that was verified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the session was valid and completed successfully.</returns>
    Task<bool> CompleteSessionAsync(AccountId accountId, string sessionCode, CancellationToken cancellationToken);
}
