using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCExpeditionShopHistoriesPacket(sbyte historiesCount, string memberName, int @type, int stack, ulong moneyAmount, long purchaseDate) : GamePacket(SCOffsets.SCExpeditionShopHistoriesPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(historiesCount);
        stream.Write(memberName);
        stream.Write(@type);
        stream.Write(stack);
        stream.Write(moneyAmount);
        stream.Write(purchaseDate);
        return stream;
    }
}
