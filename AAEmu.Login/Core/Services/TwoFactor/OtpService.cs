using AAEmu.Login.Models;
using OtpNet;

namespace AAEmu.Login.Core.Services.TwoFactor;

public class OtpService(ITwoFactorRepository repository, ILogger<OtpService> logger) : IOtpService
{
    private const int SecretLength = 20; // 160 bits, standard for TOTP

    public async Task<OtpValidationResult> ValidateAsync(AccountId accountId, string code,
        CancellationToken cancellationToken)
    {
        var config = await repository.GetConfigAsync(accountId, cancellationToken);

        if (config is null || string.IsNullOrEmpty(config.OtpSecret))
        {
            logger.LogWarning("OTP validation attempted for account {AccountId} with no OTP configured",
                accountId.Value);
            return new OtpValidationResult(false);
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(config.OtpSecret);
            var totp = new Totp(secretBytes, totpSize: 8);

            // Allow for time drift by checking current and adjacent time steps
            var isValid = totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));

            if (!isValid)
            {
                logger.LogDebug("OTP validation failed for account {AccountId}", accountId.Value);
            }

            return new OtpValidationResult(isValid);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating OTP for account {AccountId}", accountId.Value);
            return new OtpValidationResult(false);
        }
    }

    public string GenerateSecret()
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(SecretLength);
        return Base32Encoding.ToString(secretBytes);
    }
}
