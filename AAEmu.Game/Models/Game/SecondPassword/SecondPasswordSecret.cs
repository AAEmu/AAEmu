using System.Security.Cryptography;

namespace AAEmu.Game.Models.Game.SecondPassword;

/// <summary>
/// How the second password itself is kept: a salted PBKDF2 hash, never the password.
/// <para>
/// The password never reaches the server in the clear — the client sends the positions the player clicked —
/// but once it has been read back through the key table it is a secret like any other and is stored the same
/// way a login password would be.
/// </para>
/// </summary>
public static class SecondPasswordSecret
{
    /// <summary>Salt length in bytes; every password gets its own.</summary>
    public const int SaltBytes = 16;

    /// <summary>Derived key length in bytes.</summary>
    public const int HashBytes = 32;

    /// <summary>PBKDF2 iterations.</summary>
    public const int Iterations = 100_000;

    /// <summary>A fresh random salt.</summary>
    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltBytes);

    /// <summary>The derived key for a password and salt, base64 for storage.</summary>
    public static string Hash(string password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return Convert.ToBase64String(key);
    }

    /// <summary>Whether a password matches a stored hash, compared in constant time.</summary>
    public static bool Verify(string password, byte[] salt, string expectedHash)
    {
        if (password == null || salt == null || string.IsNullOrEmpty(expectedHash))
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(expectedHash);
        }
        catch (FormatException)
        {
            return false; // not something this class wrote
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
