using AAEmu.Login.Models;
using AAEmu.Login.Models.TwoFactor;

namespace AAEmu.Login.Core.Services.TwoFactor;

public class TwoFactorService(ITwoFactorRepository repository) : ITwoFactorService
{
    public async Task<TwoFactorRequirements> GetRequirementsAsync(AccountId accountId,
        CancellationToken cancellationToken)
    {
        var config = await repository.GetConfigAsync(accountId, cancellationToken);

        if (config is null)
        {
            return TwoFactorRequirements.None;
        }

        var methods = config.EnabledMethods;

        // Only require methods that are both enabled AND properly verified/configured
        var requiresOtp = methods.HasFlag(TwoFactorMethod.Otp)
                          && config.OtpVerified
                          && !string.IsNullOrEmpty(config.OtpSecret);

        var requiresPcCert = methods.HasFlag(TwoFactorMethod.PcCert)
                             && !string.IsNullOrEmpty(config.CertPinHash);

        var requiresArs = methods.HasFlag(TwoFactorMethod.Ars)
                          && config.ArsPhoneVerified
                          && !string.IsNullOrEmpty(config.ArsPhoneNumber);

        return new TwoFactorRequirements(requiresOtp, requiresPcCert, requiresArs);
    }
}
