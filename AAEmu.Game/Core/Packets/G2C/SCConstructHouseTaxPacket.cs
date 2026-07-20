using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCConstructHouseTaxPacket(
    uint designId,
    int heavyTaxHouseCount,
    int normalTaxHouseCount,
    bool isHeavyTaxHouse,
    int baseTaxMoneyAmount,
    int depositTaxMoneyAmount,
    int totalTaxMoneyAmount)
    : GamePacket(SCOffsets.SCConstructHouseTaxPacket, 5)
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
        return stream;
    }
}
