using System.Security.Cryptography;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

using NLog;

namespace AAEmu.Commons.Cryptography;

/// <summary>
/// Game (world) channel encryption for the 10.8.1.0 Kakao r651713 client.
/// Spec reverse-engineered from the original server binaries (crynetwork_dedicate.dll) and verified against
/// the client (crynetwork.dll):
///  * S->C: keyless length-seeded stream cipher (StoCEncrypt, == server sub_39572530). Applied to every
///    level-5 packet body, immediately (no key exchange needed for the S->C direction).
///  * Key exchange: X2EnterWorldResponse carries the RSA-1024 public key; the client returns its AES + XOR
///    keys RSA-encrypted in CSAesXorKey. Those keys are only needed for the C->S direction.
/// </summary>
public class EncryptionManager : Singleton<EncryptionManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int DwKeySize = 1024;

    // Keyed by accountId.
    private Dictionary<ulong, ConnectionKeychain> _connectionKeys = new();

    public void Load()
    {
        _connectionKeys = new Dictionary<ulong, ConnectionKeychain>();
        Logger.Info("Loaded Encryption Manager.");
    }

    private ConnectionKeychain GetConnectionKeys(uint connectionId, ulong accountId)
    {
        if (_connectionKeys.TryGetValue(accountId, out var keys) && keys.ConnectionId == connectionId)
            return keys;
        return GenerateRsaKeyPair(connectionId, accountId);
    }

    private ConnectionKeychain GenerateRsaKeyPair(uint connectionId, ulong accountId)
    {
        _connectionKeys.Remove(accountId);
        var rsa = RSA.Create(DwKeySize);
        var keys = new ConnectionKeychain(connectionId, rsa);
        _connectionKeys[accountId] = keys;
        return keys;
    }

    /// <summary>
    /// Writes the 260-byte public-key blob the client expects in X2EnterWorldResponse.pubKey.
    /// Client parser (sub_39592EF0) requires pubKeySize == 260; the blob layout (verified) is:
    ///   dwKeySize(int=1024) | Modulus(128) | 125 zero bytes | Exponent(3) == 260 bytes.
    /// </summary>
    public PacketStream WriteKeyParams(uint connectionId, ulong accountId, PacketStream stream)
    {
        var keychain = GenerateRsaKeyPair(connectionId, accountId);
        var p = keychain.RsaKeyPair.ExportParameters(false);
        stream.Write(DwKeySize);      // dwKeySize (int) = 1024  (4)
        stream.Write(p.Modulus);      // RSA-1024 modulus       (128)
        stream.Write(new byte[125]);  // padding                (125)
        stream.Write(p.Exponent);     // public exponent        (3, e.g. 01 00 01)
        return stream;
    }

    /// <summary>Public key blob length (pubKeySize) — must be exactly 260 for r651713.</summary>
    public const ushort PubKeySize = 260;

    /// <summary>
    /// Receives the client's RSA-encrypted AES + XOR keys (CSAesXorKey), RSA-decrypts them and derives the
    /// per-connection XOR keys. The AES/XOR material is used only for the C->S direction.
    /// </summary>
    public void StoreClientKeys(byte[] aesKeyEncrypted, byte[] xorKeyEncrypted, ulong accountId, uint connectionId)
    {
        if (!_connectionKeys.TryGetValue(accountId, out var keys))
        {
            Logger.Warn("StoreClientKeys: no keychain for accountId {0}", accountId);
            return;
        }

        try
        {
            var xorRaw = keys.RsaKeyPair.Decrypt(xorKeyEncrypted, RSAEncryptionPadding.Pkcs1);
            keys.AesKey = keys.RsaKeyPair.Decrypt(aesKeyEncrypted, RSAEncryptionPadding.Pkcs1);
            keys.XorRaw = xorRaw;

            var head = BitConverter.ToUInt32(xorRaw, 0);
            keys.Head = head;
            // Binary key derivation (client sub_39573D20 / server sub_39573C10).
            keys.XorKey1 = unchecked(head * (head ^ 0x15A02403u) ^ 0x070F1F23u);
            keys.XorKey2 = unchecked(head * (head ^ 0xFF217A82u) ^ 0x1F23070Fu);
            keys.ReceivedKeys = true;
            Logger.Info("StoreClientKeys ok acc={0} conn={1} head={2:X8}", accountId, connectionId, head);
        }
        catch (Exception e)
        {
            Logger.Error(e, "StoreClientKeys: RSA decrypt failed (acc={0})", accountId);
        }
    }

    public byte GetSCMessageCount(uint connectionId, ulong accountId) =>
        GetConnectionKeys(connectionId, accountId).SCMessageCount;

    public void IncSCMsgCount(uint connectionId, ulong accountId) =>
        GetConnectionKeys(connectionId, accountId).SCMessageCount++;

    /// <summary>Packet checksum used in the level-5 body: c = c * 0x13 + b over every byte.</summary>
    public byte Crc8(byte[] data)
    {
        uint checksum = 0;
        foreach (var b in data)
        {
            checksum *= 0x13;
            checksum += b;
        }
        return (byte)checksum;
    }

    #region S->C StoC stream cipher (keyless, length-seeded) — verified == server sub_39572530
    private static byte Inline(ref uint cry)
    {
        cry += 0x2FCBD5u;
        var n = (byte)((cry >> 16) & 0xF7);
        return n == 0 ? (byte)0xFE : n;
    }

    public byte[] StoCEncrypt(byte[] body)
    {
        var length = body.Length;
        var cry = (uint)(length ^ 0x1F2175A0);
        var array = new byte[length];
        var n = 4 * (length / 4);
        for (var i = n - 1; i >= 0; i--)
            array[i] = (byte)(body[i] ^ Inline(ref cry));
        for (var i = n; i < length; i++)
            array[i] = (byte)(body[i] ^ Inline(ref cry));
        return array;
    }
    #endregion

    #region C->S decryption (DecodeXor + AES-128-CBC) — 10.0.2.13, ported from the verified
    // PacketDecodeUniversal case 27 (constants extracted from client crynetwork.dll C2S-encrypt sub_3957AD70
    // / MakeSeq sub_3957AC50). Frame: [len u16][unk=00][level=05][hash 1B][AES cipher N*16]. The decrypted
    // plaintext is [count 1B][type u16 LE][body...]; msgKey is the real plaintext length (rest is padding).

    private static readonly int[] HashMap = BuildHashMap();
    private static int[] BuildHashMap()
    {
        var m = new int[256];
        for (var i = 0; i < 16; i++) m[0x30 + i] = i + 1; // 0x30..0x3F -> 1..16
        return m;
    }

    // Per-packet keystream step (client Add).
    private static byte Add(ref uint cry)
    {
        cry += 0x2FCBD5u;
        var n = (byte)((cry >> 16) & 0xF7);
        return n == 0 ? (byte)0xFE : n;
    }

    // Sequence accumulator (client MakeSeq sub_3957AC50). Advances per call; stored per-connection.
    private static byte MakeSeq(ConnectionKeychain k)
    {
        k.CsMSeq += 0x2FA245u;
        var result = (byte)((k.CsMSeq >> 14) & 0x73);
        return result == 0 ? (byte)0xFE : result;
    }

    // Byte-stride selector derived from the running Seq.
    private static int SeqOffset(byte seq)
    {
        if (seq == 0) return 9;
        if (seq % 3 == 0) return 5;
        if (seq % 5 == 0) return 2;
        if (seq % 7 == 0) return 11;
        if (seq % 9 == 0) return 3;
        if (seq % 11 == 0) return 7;
        return 4;
    }

    /// <summary>
    /// Decrypts a level-5 C->S frame body. <paramref name="input"/> = the frame minus the 2-byte length,
    /// i.e. [unk][level][hash][cipher...]. Returns the real plaintext [crc8][count][type u16][body], or null
    /// if keys aren't ready. Stateful — must be called in packet order per connection.
    /// </summary>
    public byte[] CSDecrypt(byte[] input, ulong accountId, uint connectionId)
    {
        // Direct lookup — do NOT use GetConnectionKeys here, it regenerates (wiping keys) on a mismatch.
        if (!_connectionKeys.TryGetValue(accountId, out var keys) || !keys.ReceivedKeys || input.Length < 4)
            return null;

        // cipher = input[3..] must be a whole number of AES blocks; otherwise this isn't a valid frame.
        var cipherLen = input.Length - 3;
        if (cipherLen <= 0 || cipherLen % 16 != 0)
            return null;

        if (keys.CsNum == 0)
        {
            keys.CsSeq = 0;
            keys.CsMSeq = 0;
            keys.IV = new byte[16];
        }

        // Verified combo (live test 2026-06-24): AES = AesKey (blob[0]), XOR = XorKey1.
        var (xored, realLen) = CsDecodeXor(input, keys, keys.XorKey1);
        var plain = CsDecodeAes(xored, keys, keys.AesKey);
        keys.CsNum++;

        if (realLen > 0 && realLen <= plain.Length)
        {
            var trimmed = new byte[realLen];
            Array.Copy(plain, 0, trimmed, 0, realLen);
            return trimmed;
        }
        return plain;
    }

    private (byte[] data, uint realLen) CsDecodeXor(byte[] bodyPacket, ConnectionKeychain k, uint xorKeyBase)
    {
        var mBody = new byte[bodyPacket.Length - 3];
        Array.Copy(bodyPacket, 3, mBody, 0, mBody.Length);

        var msgKey = (uint)(bodyPacket.Length / 16 - 1) << 4;
        msgKey += (uint)HashMap[bodyPacket[2]]; // real plaintext length

        var xorKey = unchecked(xorKeyBase * xorKeyBase); // client encrypt squares the key
        var mul = unchecked(msgKey * xorKey);
        var cry = unchecked(mul ^ ((uint)MakeSeq(k) + 0x75A02419u) ^ 0x68BEF515u);

        var offset = SeqOffset(k.CsSeq);
        var array = new byte[mBody.Length];
        var n = offset * (mBody.Length / offset);
        for (var i = n - 1; i >= 0; i--)
            array[i] = (byte)(mBody[i] ^ Add(ref cry));
        for (var i = n; i < mBody.Length; i++)
            array[i] = (byte)(mBody[i] ^ Add(ref cry));

        k.CsSeq = (byte)(k.CsSeq + MakeSeq(k) + 1);
        return (array, msgKey);
    }

    private byte[] CsDecodeAes(byte[] cipher, ConnectionKeychain k, byte[] aesKey)
    {
        var iv = (byte[])k.IV.Clone();
        var blocks = cipher.Length / 16;
        if (blocks >= 1)
        {
            k.IV = new byte[16];
            Array.Copy(cipher, (blocks - 1) * 16, k.IV, 0, 16); // next IV = last cipher block
        }

        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = aesKey;
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }
    #endregion
}
