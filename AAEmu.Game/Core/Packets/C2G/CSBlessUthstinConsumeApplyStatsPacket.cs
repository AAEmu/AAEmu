using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO: the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSBlessUthstinConsumeApplyStatsPacket() : GamePacket(CSOffsets.CSBlessUthstinConsumeApplyStatsPacket, 1)
{
    public long Item { get; private set; }
    public int PageIndex { get; private set; }

    public override void Read(PacketStream stream)
    {
        Item = stream.ReadInt64();
        PageIndex = stream.ReadInt32();
    }
}
