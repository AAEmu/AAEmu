namespace AAEmu.Login.Core.Services;

/// <summary>
/// Identifies how a password value is encoded.
/// </summary>
public enum PasswordKind
{
    /// <summary>
    /// The password is plaintext.
    /// </summary>
    Plaintext,

    /// <summary>
    /// The password is a hex-encoded SHA-256 hash.
    /// </summary>
    Sha256Hex
}

/// <summary>
/// A typed wrapper for a password value that carries its encoding.
/// </summary>
public readonly record struct Password(string Value, PasswordKind Kind)
{
    /// <summary>Creates a <see cref="Password"/> from a plaintext string.</summary>
    public static Password FromPlaintext(string value) => new(value, PasswordKind.Plaintext);

    /// <summary>Creates a <see cref="Password"/> from a hex-encoded SHA-256 string.</summary>
    public static Password FromSha256Hex(string hex) => new(hex, PasswordKind.Sha256Hex);
}
