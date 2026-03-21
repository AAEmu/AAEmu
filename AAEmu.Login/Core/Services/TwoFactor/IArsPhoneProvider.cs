namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Placeholder interface for phone provider integration.
/// </summary>
public interface IArsPhoneProvider
{
    /// <summary>
    /// Initiates a phone call to the given number with the verification code.
    /// </summary>
    /// <param name="phoneNumber">The phone number to call.</param>
    /// <param name="code">The verification code to speak.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the call was initiated successfully.</returns>
    Task<bool> InitiateCallAsync(string phoneNumber, string code, CancellationToken cancellationToken = default);
}
