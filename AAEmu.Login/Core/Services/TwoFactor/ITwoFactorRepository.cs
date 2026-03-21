using AAEmu.Login.Models;
using AAEmu.Login.Models.TwoFactor;

namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Database access for two-factor authentication configuration.
/// </summary>
public interface ITwoFactorRepository
{
    /// <summary>
    /// Gets the 2FA configuration for an account.
    /// </summary>
    /// <param name="accountId">The account ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 2FA configuration, or null if none exists.</returns>
    Task<TwoFactorConfig?> GetConfigAsync(AccountId accountId, CancellationToken cancellationToken);
}
