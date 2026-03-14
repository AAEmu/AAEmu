using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace AAEmu.Login.Core.Services;

/// <summary>
/// Implements the Unix SHA-256 crypt (<c>$5$</c>) password hashing algorithm per Drepper's specification.
/// </summary>
/// <remarks>
/// <para><b>Security note:</b> sha256_crypt at moderate rounds is weaker than PBKDF2@600k for offline attacks.
/// At 100,000 rounds it is approximately 3× weaker than PBKDF2@310k.
/// This hash is used internally for Korea challenge-response authentication only — the server never transmits
/// the hash to clients; only the salt and rounds are sent. The password is therefore not recoverable from
/// network captures, but offline dictionary attacks against a stolen DB are ~3× faster than against PBKDF2.</para>
/// <para>Each account has a unique random salt, preventing rainbow table attacks.</para>
/// </remarks>
/// <seealso href="https://akkadia.org/drepper/SHA-crypt.txt"/>
public static class Sha256Crypt
{
    private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // Byte-reordering groups for SHA-256: (i2, i1, i0, charCount)
    // Where i2 is the high byte, i1 is the mid byte, i0 is the low byte.
    // i2 == -1 means the high byte is zero (for the 2-byte final group).
    private static readonly (int i2, int i1, int i0, int n)[] ByteGroups =
    [
        (0, 10, 20, 4), (21, 1, 11, 4), (12, 22, 2, 4), (3, 13, 23, 4),
        (24, 4, 14, 4), (15, 25, 5, 4), (6, 16, 26, 4), (27, 7, 17, 4),
        (18, 28, 8, 4), (9, 19, 29, 4), (-1, 31, 30, 3),
    ];

    /// <summary>
    /// Computes the raw 32-byte SHA-256 crypt hash into <paramref name="destination"/>.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="salt">The salt string (up to 16 chars).</param>
    /// <param name="rounds">Number of stretching rounds (1000–999,999,999).</param>
    /// <param name="destination">Must be exactly 32 bytes.</param>
    public static void ComputeRaw(string password, string salt, int rounds, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(destination.Length, 32);

        var pwBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Encoding.UTF8.GetBytes(salt);

        // Step 1: digest B = SHA256(password + salt + password)
        Span<byte> sumB = stackalloc byte[32];
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha.AppendData(pwBytes);
            sha.AppendData(saltBytes);
            sha.AppendData(pwBytes);
            sha.GetHashAndReset(sumB);
        }

        // Step 2: digest A = SHA256(password + salt + (sumB repeated to fill pwLen) + bit-alternation)
        Span<byte> sumA = stackalloc byte[32];
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha.AppendData(pwBytes);
            sha.AppendData(saltBytes);

            // Add sumB in 32-byte chunks to fill password.Length bytes
            var rem = pwBytes.Length;
            while (rem > 0)
            {
                sha.AppendData(sumB[..Math.Min(rem, 32)]);
                rem -= 32;
            }

            // Bit-alternation: process bits of password length from LSB
            var bits = pwBytes.Length;
            while (bits > 0)
            {
                if ((bits & 1) != 0)
                    sha.AppendData(sumB);
                else
                    sha.AppendData(pwBytes);
                bits >>= 1;
            }

            sha.GetHashAndReset(sumA);
        }

        // Rent buffers for P-string and S-string
        //var pBuf = ArrayPool<byte>.Shared.Rent(Math.Max(pwBytes.Length, 1));
        using var pBuf = MemoryPool<byte>.Shared.Rent(Math.Max(pwBytes.Length, 1));
        using var sBuf = MemoryPool<byte>.Shared.Rent(Math.Max(saltBytes.Length, 1));

        var pString = pBuf.Memory.Span[..pwBytes.Length];
        var sString = sBuf.Memory.Span[..saltBytes.Length];

        // Step 3: P-string = SHA256(password × pwLen), expanded to pwLen bytes
        {
            Span<byte> sumP = stackalloc byte[32];
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            // Add the entire password once for each byte of the password
            for (var i = 0; i < pwBytes.Length; i++)
                sha.AppendData(pwBytes);
            sha.GetHashAndReset(sumP);
            for (var i = 0; i < pwBytes.Length; i++)
                pString[i] = sumP[i % 32];
        }

        // Step 4: S-string = SHA256(salt × (16 + sumA[0])), expanded to saltLen bytes
        {
            Span<byte> sumS = stackalloc byte[32];
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var saltReps = 16 + sumA[0];
            for (var i = 0; i < saltReps; i++)
                sha.AppendData(saltBytes);
            sha.GetHashAndReset(sumS);
            for (var i = 0; i < saltBytes.Length; i++)
                sString[i] = sumS[i % 32];
        }

        // Step 5: Stretch for `rounds` iterations
        for (var c = 0; c < rounds; c++)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if ((c & 1) != 0)
                sha.AppendData(pString);
            else
                sha.AppendData(sumA);

            if (c % 3 != 0)
                sha.AppendData(sString);

            if (c % 7 != 0)
                sha.AppendData(pString);

            if ((c & 1) != 0)
                sha.AppendData(sumA);
            else
                sha.AppendData(pString);

            sha.GetHashAndReset(sumA);
        }

        sumA.CopyTo(destination);
    }

    /// <summary>
    /// Formats a pre-computed 32-byte raw hash into the <c>$5$rounds=N$salt$...</c> string.
    /// Does not recompute the hash — use when you already have the raw bytes.
    /// </summary>
    public static string FormatEncoded(ReadOnlySpan<byte> rawHash, string salt, int rounds)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(rawHash.Length, 32);

        var sb = new StringBuilder(128);
        sb.Append("$5$rounds=");
        sb.Append(rounds);
        sb.Append('$');
        sb.Append(salt);
        sb.Append('$');

        foreach (var (i2, i1, i0, n) in ByteGroups)
        {
            var b2 = i2 >= 0 ? rawHash[i2] : (byte)0;
            var b1 = rawHash[i1];
            var b0 = rawHash[i0];
            var w = (b2 << 16) | (b1 << 8) | b0;
            for (var k = 0; k < n; k++)
            {
                sb.Append(Alphabet[w & 63]);
                w >>= 6;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a 16-character random salt using cryptographically secure random bytes.
    /// </summary>
    public static string GenerateSalt()
    {
        const string SaltChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789./";
        Span<char> salt = stackalloc char[16];
        Span<byte> rng = stackalloc byte[16];
        RandomNumberGenerator.Fill(rng);
        for (var i = 0; i < 16; i++)
            salt[i] = SaltChars[rng[i] % SaltChars.Length];
        return new string(salt);
    }

    /// <summary>
    /// Parses only the rounds value from a stored <c>$5$rounds=N$salt$hash</c> string.
    /// Cheaper than <see cref="Parse"/> when only the round count is needed (e.g. rehash checks).
    /// </summary>
    public static int ParseRounds(string stored)
    {
        if (!stored.StartsWith("$5$"))
            throw new FormatException("Not a SHA-256 crypt hash ($5$ prefix missing).");

        var rest = stored.AsSpan(3);
        if (!rest.StartsWith("rounds="))
            return 5000;

        var sepIdx = rest.IndexOf('$');
        if (sepIdx < 0)
            throw new FormatException("Malformed $5$ hash: missing '$' after rounds.");

        return int.Parse(rest[7..sepIdx]);
    }

    /// <summary>
    /// Parses a stored <c>$5$rounds=N$salt$hash</c> string and writes the 32-byte raw hash
    /// into <paramref name="rawHashDestination"/>.
    /// </summary>
    /// <param name="stored">The full <c>$5$…</c> string from the database.</param>
    /// <param name="rawHashDestination">Must be exactly 32 bytes. Receives the decoded raw hash.</param>
    /// <returns>A tuple of (rounds, salt).</returns>
    public static (int rounds, string salt) Parse(string stored, Span<byte> rawHashDestination)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(rawHashDestination.Length, 32);

        if (!stored.StartsWith("$5$"))
            throw new FormatException("Not a SHA-256 crypt hash ($5$ prefix missing).");

        var rest = stored.AsSpan(3);
        var rounds = 5000;

        if (rest.StartsWith("rounds="))
        {
            var sepIdx = rest.IndexOf('$');
            if (sepIdx < 0)
                throw new FormatException("Malformed $5$ hash: missing '$' after rounds.");
            rounds = int.Parse(rest[7..sepIdx]);
            rest = rest[(sepIdx + 1)..];
        }

        var saltEnd = rest.IndexOf('$');
        if (saltEnd < 0)
            throw new FormatException("Malformed $5$ hash: missing '$' after salt.");

        var salt = new string(rest[..saltEnd]);
        Decode(rest[(saltEnd + 1)..], rawHashDestination);
        return (rounds, salt);
    }

    /// <summary>
    /// Verifies a plaintext password against a stored <c>$5$</c> hash string (constant-time).
    /// Uses <c>stackalloc byte[32]</c> internally — no heap allocation.
    /// </summary>
    public static bool Verify(string plaintext, string stored)
    {
        Span<byte> expectedRaw = stackalloc byte[32];
        var (rounds, salt) = Parse(stored, expectedRaw);
        Span<byte> computedRaw = stackalloc byte[32];
        ComputeRaw(plaintext, salt, rounds, computedRaw);
        return CryptographicOperations.FixedTimeEquals(computedRaw, expectedRaw);
    }

    /// <summary>
    /// Decodes the 43-character base64 portion of a <c>$5$</c> hash into <paramref name="destination"/>.
    /// </summary>
    private static void Decode(ReadOnlySpan<char> hash, Span<byte> destination)
    {
        if (hash.Length != 43)
            throw new FormatException($"Expected 43-char hash, got {hash.Length}.");

        var charIdx = 0;

        foreach (var (i2, i1, i0, n) in ByteGroups)
        {
            var w = 0;
            for (var k = 0; k < n; k++)
            {
                var c = hash[charIdx++];
                var v = Alphabet.IndexOf(c);
                if (v < 0) throw new FormatException($"Invalid character '{c}' in SHA-256 crypt hash.");
                w |= v << (6 * k);
            }

            if (i2 >= 0) destination[i2] = (byte)(w >> 16);
            destination[i1] = (byte)((w >> 8) & 0xFF);
            destination[i0] = (byte)(w & 0xFF);
        }
    }
}
