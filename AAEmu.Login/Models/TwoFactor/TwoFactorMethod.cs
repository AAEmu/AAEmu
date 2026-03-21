namespace AAEmu.Login.Models.TwoFactor;

/// <summary>
/// Two-factor authentication methods that can be enabled for an account.
/// </summary>
[Flags]
public enum TwoFactorMethod : byte
{
    /// <summary>
    /// No two-factor authentication enabled.
    /// </summary>
    None = 0,

    /// <summary>
    /// Time-based one-time password (TOTP) via authenticator app.
    /// </summary>
    Otp = 1,

    /// <summary>
    /// PC certificate PIN verification.
    /// </summary>
    PcCert = 2,

    /// <summary>
    /// ARS phone callback verification.
    /// </summary>
    Ars = 4
}
