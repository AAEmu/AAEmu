using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseTaxInfoPacket(
    ushort tl,
    int dominionTaxRate,
    int moneyAmount,
    int moneyAmount2,
    DateTime due,
    bool isAlreadyPaid,
    int weeksWithoutPay,
    bool isHeavyTaxHouse)
    : GamePacket(SCOffsets.SCHouseTaxInfoPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(dominionTaxRate);
        stream.Write(moneyAmount);
        stream.Write(moneyAmount2);
        stream.Write(due);
        stream.Write(isAlreadyPaid);
        stream.Write(weeksWithoutPay);
        stream.Write(isHeavyTaxHouse);
        return stream;
    }
}
