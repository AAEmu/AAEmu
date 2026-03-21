namespace AAEmu.Login.Models.TwoFactor;

/// <summary>
/// Complete two-factor authentication configuration for an account.
/// </summary>
/// <param name="UserId">The account ID this configuration belongs to.</param>
/// <param name="EnabledMethods">Bitmask of enabled 2FA methods.</param>
/// <param name="OtpSecret">Base32-encoded TOTP secret, or null if not configured.</param>
/// <param name="OtpVerified">Whether OTP setup has been verified.</param>
/// <param name="CertPinHash">Hashed PIN for PC certificate, or null if not configured.</param>
/// <param name="ArsPhoneNumber">Phone number for ARS callback, or null if not configured.</param>
/// <param name="ArsPhoneVerified">Whether ARS phone has been verified.</param>
/// <param name="CreatedAt">Unix timestamp when 2FA was first configured.</param>
/// <param name="UpdatedAt">Unix timestamp when 2FA was last updated.</param>
public record TwoFactorConfig(
    AccountId UserId,
    TwoFactorMethod EnabledMethods,
    string? OtpSecret,
    bool OtpVerified,
    string? CertPinHash,
    string? ArsPhoneNumber,
    bool ArsPhoneVerified,
    long CreatedAt,
    long UpdatedAt);
