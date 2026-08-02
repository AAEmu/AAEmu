using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AAEmu.Login.Core.Services;

/// <summary>
/// Pure-static AES-based challenge-response verifier for the Korea login protocol.
/// </summary>
/// <remarks>
/// The challenge response uses the following computation:
/// <code>
/// K              = sha256_crypt(password, "$5$rounds=N$salt")  // 32-byte raw hash = AES-256 key
/// t              = AES_CBC_decrypt(K, IV=zeros, hc_bytes)
/// t_uint32[i]   += 1                                           // increment each DWORD
/// ch_response[:] = AES_CBC_encrypt(K, IV=zeros, t)
/// </code>
/// The server recomputes <c>ch_response</c> using the stored key material and the
/// received <c>hc</c> values, then compares with the received <c>ch</c>.
/// </remarks>
public static class KoreanChallengeVerifier
{
    /// <summary>
    /// V1 Korea challenge verifier (AES-192, SHA-1-based key).
    /// </summary>
    /// <remarks>
    /// <b>NOT ENABLED IN PRODUCTION.</b> SHA-1 without a per-account salt is approximately
    /// 17 million times weaker than PBKDF2@600k for offline attacks. This method exists for
    /// protocol completeness only. The server never issues <c>ACChallengePacket</c> (V1).
    /// </remarks>
    [Obsolete("V1 Korea challenge is disabled due to SHA-1 security weaknesses. Use V2 only.", true)]
    internal static bool VerifyV1(ReadOnlySpan<byte> sha1Hash, uint challengeSalt,
        ReadOnlySpan<uint> hc, ReadOnlySpan<uint> responseCh)
    {
        if (sha1Hash.Length != 20 || hc.Length != 4 || responseCh.Length != 4)
            return false;

        // K = SHA1(password)[0..19] || challengeSalt_bytes[0..3] → 24 bytes = AES-192
        using var key = MemoryPool<byte>.Shared.Rent(24);
        var keySpan = key.Memory.Span;

        sha1Hash.CopyTo(keySpan[..20]);
        BinaryPrimitives.WriteUInt32LittleEndian(keySpan.Slice(20, 4), challengeSalt);
        return VerifyCore(keySpan[..24], hc, responseCh);
    }

    /// <summary>
    /// Verifies a V2 Korea challenge response (AES-256, sha256_crypt-based key).
    /// </summary>
    /// <param name="challengeKey">32-byte raw sha256_crypt hash used as the AES-256 key.</param>
    /// <param name="hc">8 server-generated challenge values sent in <c>ACChallenge2Packet</c>.</param>
    /// <param name="responseCh">8 client-computed challenge response values from <c>CAChallengeResponse2Packet</c>.</param>
    /// <returns><c>true</c> if the response is cryptographically correct.</returns>
    public static bool VerifyV2(ReadOnlySpan<byte> challengeKey,
        ReadOnlySpan<uint> hc, ReadOnlySpan<uint> responseCh)
    {
        if (challengeKey.Length != 32 || hc.Length != 8 || responseCh.Length != 8)
            return false;

        return VerifyCore(challengeKey, hc, responseCh);
    }

    private static bool VerifyCore(ReadOnlySpan<byte> key,
        ReadOnlySpan<uint> hc, ReadOnlySpan<uint> expectedCh)
    {
        var blockCount = hc.Length; // 4 for V1 (16 bytes), 8 for V2 (32 bytes)
        var byteCount = blockCount * 4;

        using var tBuf = MemoryPool<byte>.Shared.Rent(byteCount);
        var hcBytesBuf = MemoryPool<byte>.Shared.Rent(byteCount);
        var expectedBuf = MemoryPool<byte>.Shared.Rent(byteCount);

        var t = tBuf.Memory.Span[..byteCount];
        var hcBytes = hcBytesBuf.Memory.Span[..byteCount];
        var expectedChBytes = expectedBuf.Memory.Span[..byteCount];

        // Step 1: marshal hc uints → bytes (little-endian)
        for (var i = 0; i < blockCount; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(hcBytes.Slice(i * 4, 4), hc[i]);

        // Step 2: t = AES_CBC_decrypt(K, IV=zeros, hc_bytes)
        ApplyCbcDecryptZeroIv(key, hcBytes, t);

        // Step 3: each uint32 in t += 1
        for (var i = 0; i < blockCount; i++)
        {
            var val = BinaryPrimitives.ReadUInt32LittleEndian(t.Slice(i * 4, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(t.Slice(i * 4, 4), unchecked(val + 1));
        }

        // Step 4: ch_computed = AES_CBC_encrypt(K, IV=zeros, t) → reuse hcBytes buffer (hc input no longer needed)
        ApplyCbcEncryptZeroIv(key, t, hcBytes);

        // Step 5: marshal expected ch uints → bytes and compare (constant-time)
        for (var i = 0; i < blockCount; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(expectedChBytes.Slice(i * 4, 4), expectedCh[i]);

        return CryptographicOperations.FixedTimeEquals(hcBytes, expectedChBytes);
    }

    private static void ApplyCbcDecryptZeroIv(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> iv = stackalloc byte[16];
        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.DecryptCbc(input, iv, output, PaddingMode.None);
    }

    private static void ApplyCbcEncryptZeroIv(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> iv = stackalloc byte[16]; // zero-initialised
        using var aes = Aes.Create();
        aes.SetKey(key);
        aes.EncryptCbc(input, iv, output, PaddingMode.None);
    }
}
