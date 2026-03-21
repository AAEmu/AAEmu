using AAEmu.Login.Models;
using AAEmu.Login.Models.TwoFactor;

namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Main entry point for querying 2FA requirements during authentication.
/// </summary>
public interface ITwoFactorService
{
    /// <summary>
    /// Gets the 2FA requirements for an account.
    /// </summary>
    /// <param name="accountId">The account ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 2FA requirements indicating which methods are required.</returns>
    Task<TwoFactorRequirements> GetRequirementsAsync(AccountId accountId, CancellationToken cancellationToken);
}
