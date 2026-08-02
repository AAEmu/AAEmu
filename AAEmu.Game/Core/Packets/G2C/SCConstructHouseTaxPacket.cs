using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// four u64 moneyAmount and u32 hostileTaxRate.
/// </summary>
/// <remarks>
/// The money fields widened to 64 bits and gained a fourth entry, and the rate is new. Writing the
/// v1.2 shape ran the client out of buffer mid-moneyAmount, so it discarded the quote and never sent
/// the build request — the placement preview stayed on the ground and nothing else happened. The
/// binary names all four money fields identically, so the fourth is appended after the total and
/// carries the weekly amount CalculateBuildingTaxInfo already computes.
/// </remarks>
public class SCConstructHouseTaxPacket(
    uint designId,
    int heavyTaxHouseCount,
    int normalTaxHouseCount,
    bool isHeavyTaxHouse,
    ulong baseTaxMoneyAmount,
    ulong depositTaxMoneyAmount,
    ulong totalTaxMoneyAmount,
    ulong weeklyTaxMoneyAmount,
    uint hostileTaxRate)
    : GamePacket(SCOffsets.SCConstructHouseTaxPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(designId);
        stream.Write(heavyTaxHouseCount);
        stream.Write(normalTaxHouseCount);
        stream.Write(isHeavyTaxHouse);
        stream.Write(baseTaxMoneyAmount);
        stream.Write(depositTaxMoneyAmount);
        stream.Write(totalTaxMoneyAmount);
        stream.Write(weeklyTaxMoneyAmount);
        stream.Write(hostileTaxRate);
        return stream;
    }
}
