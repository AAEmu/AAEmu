using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// bool isAlreadyPaid, i8 weeksWithoutPay, i8 weeksPrepay, bool isHeavyTaxHouse, i8 taxType.
/// </summary>
/// <remarks>
/// The money pair widened to 64 bits and weeksWithoutPay narrowed to a byte, and hostileTaxRate,
/// weeksPrepay and taxType are all new. Sending the v1.2 shape left the client short by the time it
/// reached <c>due</c>, so the panel a house's name plaque opens never populated.
/// </remarks>
public class SCHouseTaxInfoPacket(
    ushort tl,
    uint dominionTaxRate,
    uint hostileTaxRate,
    ulong moneyAmount,
    ulong moneyAmount2,
    DateTime due,
    bool isAlreadyPaid,
    sbyte weeksWithoutPay,
    byte weeksPrepay,
    bool isHeavyTaxHouse,
    byte taxType)
    : GamePacket(SCOffsets.SCHouseTaxInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(dominionTaxRate);
        stream.Write(hostileTaxRate);
        stream.Write(moneyAmount);
        stream.Write(moneyAmount2);
        stream.Write(due);
        stream.Write(isAlreadyPaid);
        stream.Write(weeksWithoutPay);
        stream.Write(weeksPrepay);
        stream.Write(isHeavyTaxHouse);
        stream.Write(taxType);
        return stream;
    }
}
