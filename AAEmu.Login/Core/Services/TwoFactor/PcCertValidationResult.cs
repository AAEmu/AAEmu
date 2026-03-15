namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Result of PC certificate PIN validation.
/// </summary>
/// <param name="Success">Whether the PIN was valid.</param>
public readonly record struct PcCertValidationResult(bool Success);
