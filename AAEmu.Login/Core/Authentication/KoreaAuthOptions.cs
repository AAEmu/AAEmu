using System.ComponentModel.DataAnnotations;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Configuration options for Korean authentication flow.
/// </summary>
public class KoreaAuthOptions
{
    /// <summary>
    /// The maximum number of OTP entry attempts before denying authentication.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxOtpAttempts { get; set; } = 3;

    /// <summary>
    /// The maximum number of certificate entry attempts before denying authentication.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxCertAttempts { get; set; } = 3;

    /// <summary>
    /// The timeout for ARS (phone verification).
    /// </summary>
    public TimeSpan ArsTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
