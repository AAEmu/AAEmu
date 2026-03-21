using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Services.TwoFactor;

public class PcCertService(
    ITwoFactorRepository repository,
    IPasswordService passwordService,
    ILogger<PcCertService> logger) : IPcCertService
{
    public async Task<PcCertValidationResult> ValidateAsync(AccountId accountId, string pin,
        CancellationToken cancellationToken)
    {
        var config = await repository.GetConfigAsync(accountId, cancellationToken);

        if (config is null || string.IsNullOrEmpty(config.CertPinHash))
        {
            logger.LogWarning("PcCert validation attempted for account {AccountId} with no PIN configured",
                accountId.Value);
            return new PcCertValidationResult(false);
        }

        // Reuse the password service for PIN verification - same hashing approach
        var result = passwordService.VerifyPassword(config.CertPinHash, Password.FromPlaintext(pin));
        var isValid = result != PasswordVerificationResult.Failed;

        if (!isValid)
        {
            logger.LogDebug("PcCert PIN validation failed for account {AccountId}", accountId.Value);
        }

        return new PcCertValidationResult(isValid);
    }
}
