using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Account payment state. Field order, offsets and widths verified against the 10.0.2.13 client's
/// serializer at rva 0xc512a0, which names each value as it reads it:
/// payMethod (+0x10, int32), payLocation (+0x14, int32), payStart (+0x18, DateTime),
/// payEnd (+0x20, DateTime), realPayTime (+0x28, int64), buyPremiumCount (+0x30, int32).
/// </summary>
/// <remarks>
/// payStart and payEnd go through the client's DateTime slot (0x78) while realPayTime goes through the
/// plain int64 slot (0x98) - so realPayTime is a COUNT, not a timestamp, and it was hardcoded to zero
/// along with buyPremiumCount. Both are now supplied by the caller.
/// </remarks>
public class SCAccountInfoPacket(
    int payMethod,
    int payLocation,
    DateTime payStart,
    DateTime payEnd,
    long realPayTime = 0,
    int buyPremiumCount = 0)
    : GamePacket(SCOffsets.SCAccountInfoPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(payMethod);
        stream.Write(payLocation);
        stream.Write(payStart);
        stream.Write(payEnd);
        stream.Write(realPayTime);
        stream.Write(buyPremiumCount);
        return stream;
    }
}
