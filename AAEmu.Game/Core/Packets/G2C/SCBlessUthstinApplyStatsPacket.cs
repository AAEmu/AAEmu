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
public class SCBlessUthstinApplyStatsPacket(uint bc, bool bResult, uint stats, int targetPageIndex, uint normalApplyCount, uint specialApplyCount, bool bLogin) : GamePacket(SCOffsets.SCBlessUthstinApplyStatsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(bResult);
        stream.Write(stats);
        stream.Write(targetPageIndex);
        stream.Write(normalApplyCount);
        stream.Write(specialApplyCount);
        stream.Write(bLogin);
        return stream;
    }
}
