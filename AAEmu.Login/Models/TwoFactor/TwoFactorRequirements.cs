namespace AAEmu.Login.Models.TwoFactor;

/// <summary>
/// Specifies which 2FA steps are required for authentication.
/// </summary>
/// <param name="RequiresOtp">Whether TOTP verification is required.</param>
/// <param name="RequiresPcCert">Whether PC certificate PIN verification is required.</param>
/// <param name="RequiresArs">Whether ARS phone callback verification is required.</param>
public readonly record struct TwoFactorRequirements(
    bool RequiresOtp,
    bool RequiresPcCert,
    bool RequiresArs)
{
    /// <summary>
    /// No 2FA required.
    /// </summary>
    public static TwoFactorRequirements None => new(false, false, false);

    /// <summary>
    /// Returns true if any 2FA method is required.
    /// </summary>
    public bool AnyRequired => RequiresOtp || RequiresPcCert || RequiresArs;
}
