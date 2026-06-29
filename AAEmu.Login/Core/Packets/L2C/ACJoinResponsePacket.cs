using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

public enum JoinResponseReason : ushort
{
    Success = 0,
    ProtocolMismatch = 1,
    ModeMismatch = 2
}

/// <summary>
/// AFS (auth feature settings) bitfield delivered to the client in ACJoinResponse. The 10.0.2.13 client
/// decodes it (x2game AuthClient feature-set reader sub_398A5F10) as: byte0 = chCountLimit (base creatable
/// character count), byte1 = chMaxCountLimit (max characters), byte2 = chCountWorldLimit (per-world count),
/// bit24 = waitInWorld, bit25 = premiumEntrance, bit26 = b2pService. chMaxCountLimit MUST be >= 1, otherwise
/// the client treats the account's creatable slot count as 0 and every empty slot shows "creation unavailable".
/// </summary>
/// <param name="ChCountLimit">Base creatable character count.</param>
/// <param name="ChMaxCountLimit">Maximum characters per account.</param>
/// <param name="ChCountWorldLimit">Maximum characters per world.</param>
public readonly record struct AfsValue(
    byte ChCountLimit,
    byte ChMaxCountLimit,
    byte ChCountWorldLimit,
    bool WaitInWorld = false,
    bool PremiumEntrance = false,
    bool B2pService = false)
{
    public static AfsValue FromULong(ulong afs) => new(
        (byte)(afs & 0xFF),
        (byte)((afs >> 8) & 0xFF),
        (byte)((afs >> 16) & 0xFF),
        (afs & (1UL << 24)) != 0,
        (afs & (1UL << 25)) != 0,
        (afs & (1UL << 26)) != 0);

    public ulong ToULong()
    {
        var afs = (ulong)ChCountLimit
                  | ((ulong)ChMaxCountLimit << 8)
                  | ((ulong)ChCountWorldLimit << 16);
        if (WaitInWorld)
            afs |= 1UL << 24;
        if (PremiumEntrance)
            afs |= 1UL << 25;
        if (B2pService)
            afs |= 1UL << 26;
        return afs;
    }
}

/// <summary>
/// A packet sent by the login server to the client in response to a successful authentication request.
/// </summary>
/// <param name="reason"></param>
/// <param name="afs"></param>
public class ACJoinResponsePacket(ushort reason, ulong afs, byte authId = 0)
    : LoginPacket(LCOffsets.ACJoinResponsePacket)
{
    public ACJoinResponsePacket(JoinResponseReason reason, AfsValue afs) : this((ushort)reason, afs.ToULong())
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(authId);
        stream.Write(reason);
        stream.Write(afs); // 10.0.2.13 feature-set qword: see AfsValue (byte0 chCountLimit / byte1 chMaxCountLimit / byte2 chCountWorldLimit)

        return stream;
    }
}
