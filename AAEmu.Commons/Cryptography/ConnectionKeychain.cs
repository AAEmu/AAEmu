using System.Security.Cryptography;

namespace AAEmu.Commons.Cryptography;

/// <summary>
/// Per-connection key material for the encrypted game (world) channel of the
/// 10.8.1.0 Kakao r651713 client. The server generates an RSA-1024 key pair, sends the public key in
/// X2EnterWorldResponse, and the client returns its AES + XOR keys (RSA-encrypted) in CSAesXorKey.
/// </summary>
public class ConnectionKeychain(uint connectionId, RSA rsaKeyPair)
{
    public uint ConnectionId { get; set; } = connectionId;
    public RSA RsaKeyPair { get; set; } = rsaKeyPair;

    /// <summary>True once the client's CSAesXorKey has been received and decrypted.</summary>
    public bool ReceivedKeys { get; set; }

    /// <summary>16-byte AES key recovered from CSAesXorKey (used for C->S AES-128-CBC).</summary>
    public byte[] AesKey { get; set; } = new byte[16];

    /// <summary>Raw 16-byte second key blob (the "XOR" key, head source). Kept for decrypt diagnostics.</summary>
    public byte[] XorRaw { get; set; } = new byte[16];

    /// <summary>AES-CBC IV (last ciphertext block of the previous C->S packet).</summary>
    public byte[] IV { get; set; } = new byte[16];

    /// <summary>Raw XOR "head" dword recovered from CSAesXorKey.</summary>
    public uint Head { get; set; }

    // Derived XOR keys (binary: client sub_39573D20 / server sub_39573C10). Used for C->S DecodeXor.
    public uint XorKey1 { get; set; }
    public uint XorKey2 { get; set; }

    // Per-direction packet counters used by the level-5 framing / decryption sequencing.
    public byte SCMessageCount { get; set; }
    public byte CSMessageCount { get; set; }
    public byte CSOffsetSequence { get; set; }
    public uint CSSecondaryOffsetSequence { get; set; }

    // C->S DecodeXor running state (10.0.2.13). Reset when CsNum == 0 (first encrypted C->S packet).
    public byte CsSeq { get; set; }   // "Seq" — stride selector, advances per packet
    public uint CsMSeq { get; set; }  // "m_seq" — MakeSeq accumulator
    public uint CsNum { get; set; }   // packet counter; IV + Seq + m_seq reset at 0
}
