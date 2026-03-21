namespace AAEmu.Login.Core.Services;

/// <summary>
/// Helpers for computing and verifying the sha256_crypt (<c>$5$</c>) hashes used in the Korea
/// V2 challenge-response authentication protocol.
/// <para>
/// These are specific to the game-client ↔ login-server protocol and have no place in a generic
/// auth library — they live here alongside <see cref="KoreanChallengeVerifier"/> and
/// <see cref="Sha256Crypt"/>.
/// </para>
/// </summary>
public static class KoreaChallengeCrypt
{
    /// <summary>
    /// Computes a sha256_crypt hash for a plaintext password, writing the raw 32-byte hash
    /// (the AES-256 key used by the client) into <paramref name="rawHashDestination"/> and
    /// returning the full <c>$5$rounds=N$salt$…</c> string suitable for DB storage.
    /// </summary>
    /// <param name="plaintext">The plaintext password.</param>
    /// <param name="rawHashDestination">Must be exactly 32 bytes.</param>
    /// <param name="rounds">Number of sha256_crypt stretching rounds.</param>
    /// <returns>The full <c>$5$…</c> encoded string for the <c>korea_challenge_hash</c> column.</returns>
    public static string Compute(string plaintext, Span<byte> rawHashDestination, int rounds)
    {
        var salt = Sha256Crypt.GenerateSalt();
        Sha256Crypt.ComputeRaw(plaintext, salt, rounds, rawHashDestination);
        return Sha256Crypt.FormatEncoded(rawHashDestination, salt, rounds);
    }

    /// <summary>
    /// Convenience overload for callers that only need the encoded string and have no use for
    /// the raw hash bytes (e.g. seed-data tooling).
    /// </summary>
    public static string Compute(string plaintext, int rounds)
    {
        Span<byte> rawKey = stackalloc byte[32];
        return Compute(plaintext, rawKey, rounds);
    }

    /// <summary>
    /// Verifies a plaintext password against a stored <c>korea_challenge_hash</c> (constant-time).
    /// Useful when plaintext is available without performing a full challenge-response exchange.
    /// </summary>
    public static bool Verify(string stored, string plaintext) =>
        Sha256Crypt.Verify(plaintext, stored);
}