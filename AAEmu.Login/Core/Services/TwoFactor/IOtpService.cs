using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Service for TOTP (Time-based One-Time Password) operations.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Validates a TOTP code for an account.
    /// </summary>
    /// <param name="accountId">The account ID to validate against.</param>
    /// <param name="code">The 6-digit TOTP code entered by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<OtpValidationResult> ValidateAsync(AccountId accountId, string code, CancellationToken cancellationToken);

    /// <summary>
    /// Generates a new TOTP secret for account setup.
    /// </summary>
    /// <returns>A Base32-encoded secret suitable for authenticator apps.</returns>
    string GenerateSecret();
}
