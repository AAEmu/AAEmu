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
public class SCLoadCraftOrderEntryPacket(uint count, ulong @type, sbyte unnamed1, sbyte kind, ulong @type2, long orderItemId, int @type3, uint craftCount, sbyte @type4, ulong moneyAmount, int @type5, uint actabilityPoint, long postDate, long expireDate, sbyte unnamed2, ulong @type6) : GamePacket(SCOffsets.SCLoadCraftOrderEntryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(@type);
        stream.Write(unnamed1);
        stream.Write(kind);
        stream.Write(@type2);
        stream.Write(orderItemId);
        stream.Write(@type3);
        stream.Write(craftCount);
        stream.Write(@type4);
        stream.Write(moneyAmount);
        stream.Write(@type5);
        stream.Write(actabilityPoint);
        stream.Write(postDate);
        stream.Write(expireDate);
        stream.Write(unnamed2);
        stream.Write(@type6);
        return stream;
    }
}
