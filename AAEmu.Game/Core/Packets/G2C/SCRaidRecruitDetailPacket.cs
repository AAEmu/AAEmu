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
public class SCRaidRecruitDetailPacket(ulong @type, string ownerName, sbyte ownerLevel, int @type2, int @type3, int @type4, uint limitLevel, uint limitGearPoint, string msg, uint hour, uint minute, long createTime) : GamePacket(SCOffsets.SCRaidRecruitDetailPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(ownerName);
        stream.Write(ownerLevel);
        stream.Write(@type2);
        stream.Write(@type3);
        stream.Write(@type4);
        stream.Write(limitLevel);
        stream.Write(limitGearPoint);
        stream.Write(msg);
        stream.Write(hour);
        stream.Write(minute);
        stream.Write(createTime);
        return stream;
    }
}
