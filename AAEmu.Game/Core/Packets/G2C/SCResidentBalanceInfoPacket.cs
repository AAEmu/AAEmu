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
public class SCResidentBalanceInfoPacket(short @type, ulong @type2, uint memberCount, uint point, uint zonePoint, ulong moneyAmount, ulong moneyAmount2) : GamePacket(SCOffsets.SCResidentBalanceInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(memberCount);
        stream.Write(point);
        stream.Write(zonePoint);
        stream.Write(moneyAmount);
        stream.Write(moneyAmount2);
        return stream;
    }
}
