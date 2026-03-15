using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Service for PC certificate PIN verification.
/// </summary>
public interface IPcCertService
{
    /// <summary>
    /// Validates a PIN against the stored hash for an account.
    /// </summary>
    /// <param name="accountId">The account ID to validate against.</param>
    /// <param name="pin">The PIN entered by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<PcCertValidationResult> ValidateAsync(AccountId accountId, string pin, CancellationToken cancellationToken);
}
