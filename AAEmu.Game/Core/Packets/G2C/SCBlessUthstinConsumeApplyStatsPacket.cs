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
public class SCBlessUthstinConsumeApplyStatsPacket(uint bc, bool bResult, int @type, uint incStatsKind, uint decStatsKind, uint incStatsPoint, uint decStatsPoint) : GamePacket(SCOffsets.SCBlessUthstinConsumeApplyStatsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(bResult);
        stream.Write(@type);
        stream.Write(incStatsKind);
        stream.Write(decStatsKind);
        stream.Write(incStatsPoint);
        stream.Write(decStatsPoint);
        return stream;
    }
}
