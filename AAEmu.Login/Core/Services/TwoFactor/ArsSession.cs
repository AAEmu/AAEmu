namespace AAEmu.Login.Core.Services.TwoFactor;

/// <summary>
/// Represents an active ARS phone callback session.
/// </summary>
/// <param name="SessionCode">The 4-digit code displayed to the user and spoken during the call.</param>
/// <param name="ExpiresAt">When the session expires.</param>
public readonly record struct ArsSession(string SessionCode, DateTimeOffset ExpiresAt);
