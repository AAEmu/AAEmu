namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Result of OTP validation.
/// </summary>
/// <param name="Success">Whether the OTP code was valid.</param>
public readonly record struct OtpValidationResult(bool Success);
