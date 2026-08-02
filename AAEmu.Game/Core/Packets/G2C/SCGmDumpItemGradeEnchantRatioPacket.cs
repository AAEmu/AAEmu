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
public class SCGmDumpItemGradeEnchantRatioPacket(int range, sbyte @type, sbyte @type2, sbyte @type3) : GamePacket(SCOffsets.SCGmDumpItemGradeEnchantRatioPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(range);
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(@type3);
        return stream;
    }
}
