using System.Buffers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace AAEmu.Login.Core.Services;

public class PasswordService(IPasswordHasher<string> passwordHasher) : IPasswordService
{
    // Legacy format: Base64-encoded 32 bytes = exactly 44 characters (with padding)
    private const int LegacyBase64Length = 44;
    private const int LegacyHashByteLength = 32;

    public string HashForStorage(Password password) => password.Kind switch
    {
        // AAEmu Launcher sends hex-encoded SHA-256. Convert hex → bytes → Base64 so the result
        // is stored in the legacy DB format. A subsequent plaintext login can then verify it
        // via SHA-256 computation, and SuccessRehashNeeded will trigger a PBKDF2 upgrade.
        PasswordKind.Sha256Hex => Convert.ToBase64String(Convert.FromHexString(password.Value)),
        PasswordKind.Plaintext => passwordHasher.HashPassword(string.Empty, password.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(password))
    };

    public PasswordVerificationResult VerifyPassword(string storedHash, Password password)
    {
        if (IsLegacyFormat(storedHash))
        {
            var matches = password.Kind == PasswordKind.Sha256Hex
                ? VerifyLegacyPassword(storedHash, password.Value)
                : VerifyLegacyPasswordFromPlaintext(storedHash, password.Value);

            return matches
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        var result = passwordHasher.VerifyHashedPassword(string.Empty, storedHash, password.Value);
        return result switch
        {
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success => PasswordVerificationResult.Success,
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationResult
                .SuccessRehashNeeded,
            _ => PasswordVerificationResult.Failed
        };
    }

    public bool IsLegacyFormat(string storedHash)
    {
        if (storedHash.Length != LegacyBase64Length)
            return false;

        Span<byte> buffer = stackalloc byte[LegacyHashByteLength];
        return Convert.TryFromBase64String(storedHash, buffer, out var bytesWritten)
               && bytesWritten == LegacyHashByteLength;
    }

    private static bool VerifyLegacyPasswordFromPlaintext(string storedHash, string plaintext)
    {
        Span<byte> storedBytes = stackalloc byte[LegacyHashByteLength];
        if (!Convert.TryFromBase64String(storedHash, storedBytes, out var bytesWritten)
            || bytesWritten != LegacyHashByteLength)
        {
            return false;
        }

        Span<byte> computedHash = stackalloc byte[LegacyHashByteLength];
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext), computedHash);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedBytes[..bytesWritten]);
    }

    private static bool VerifyLegacyPassword(string storedHash, string passwordHex)
    {
        Span<byte> storedBytes = stackalloc byte[LegacyHashByteLength];
        if (!Convert.TryFromBase64String(storedHash, storedBytes, out var bytesWritten)
            || bytesWritten != LegacyHashByteLength)
        {
            return false;
        }

        Span<byte> passwordBytes = stackalloc byte[LegacyHashByteLength];
        if (Convert.FromHexString(passwordHex, passwordBytes, out _, out var hexBytesWritten) != OperationStatus.Done ||
            hexBytesWritten != LegacyHashByteLength)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(passwordBytes, storedBytes[..bytesWritten]);
    }
}
