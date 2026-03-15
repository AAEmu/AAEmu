namespace AAEmu.Login.Core.Services;

/// <summary>
/// Result of a password verification operation against a stored hash.
/// </summary>
public enum PasswordVerificationResult
{
    /// <summary>
    /// The password did not match.
    /// </summary>
    Failed,

    /// <summary>
    /// The password matched and is stored in the current format.
    /// </summary>
    Success,

    /// <summary>
    /// The password matched but is stored in a legacy format and should be rehashed.
    /// </summary>
    SuccessRehashNeeded
}

/// <summary>
/// Provides password hashing and verification services with support for migrating from legacy formats.
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// Produces a value suitable for storage in the database for the given password.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <returns>
    /// For <see cref="PasswordKind.Plaintext"/>, a PBKDF2 hash string.
    /// For <see cref="PasswordKind.Sha256Hex"/>, a Base64-encoded representation of the raw SHA-256 bytes
    /// (the legacy DB format), so that a subsequent plaintext login can verify it via legacy comparison.
    /// </returns>
    string HashForStorage(Password password);

    /// <summary>
    /// Verifies a password against a stored hash.
    /// </summary>
    /// <param name="storedHash">The hash stored in the database.</param>
    /// <param name="password">The password to verify, with its encoding kind.</param>
    /// <returns>
    /// <see cref="PasswordVerificationResult.Success"/> if the password matches and no rehash is needed,
    /// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> if the password matches but uses a legacy format,
    /// or <see cref="PasswordVerificationResult.Failed"/> if the password does not match.
    /// </returns>
    PasswordVerificationResult VerifyPassword(string storedHash, Password password);

    /// <summary>
    /// Determines whether a stored hash uses the legacy format (Base64-encoded raw bytes).
    /// </summary>
    /// <param name="storedHash">The hash stored in the database.</param>
    /// <returns><c>true</c> if the hash is in legacy format; otherwise, <c>false</c>.</returns>
    bool IsLegacyFormat(string storedHash);

    /// <summary>
    /// Computes a sha256_crypt (<c>$5$</c>) hash for Korea challenge-response authentication (V2 only).
    /// Writes the 32-byte raw hash (AES-256 key) into <paramref name="rawHashDestination"/>
    /// and returns the full <c>$5$rounds=N$salt$…</c> string for DB storage.
    /// </summary>
    /// <param name="plaintext">The plaintext password.</param>
    /// <param name="rawHashDestination">
    /// Must be exactly 32 bytes. Receives the raw hash that is used as the AES-256 key
    /// in <c>KoreanChallengeVerifier.VerifyV2</c>.
    /// </param>
    /// <param name="salt">
    /// The salt to use, or <c>null</c> to generate a new random 16-character salt.
    /// </param>
    /// <param name="rounds">Number of stretching rounds.</param>
    /// <returns>The full <c>$5$rounds=N$salt$…</c> encoded string for DB storage.</returns>
    string ComputeKoreaChallengeHash(string plaintext, Span<byte> rawHashDestination,
        string? salt = null, int rounds = 5000);

    /// <summary>
    /// Convenience overload for callers that only need the encoded string for DB storage
    /// and have no use for the raw hash bytes.
    /// </summary>
    /// <inheritdoc cref="ComputeKoreaChallengeHash(string, Span{byte}, string?, int)"/>
    string ComputeKoreaChallengeHash(string plaintext, string? salt = null, int rounds = 5000);

    /// <summary>
    /// Verifies a plaintext password against a stored <c>korea_challenge_hash</c> (constant-time).
    /// Useful when plaintext is available without performing a full challenge-response exchange.
    /// </summary>
    /// <param name="stored">The <c>$5$…</c> string from the DB.</param>
    /// <param name="plaintext">The plaintext password to verify.</param>
    bool VerifyKoreaChallengeHash(string stored, string plaintext);
}
