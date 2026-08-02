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
public class CSBlessUthstinApplyStatsPacket() : GamePacket(CSOffsets.CSBlessUthstinApplyStatsPacket, 1)
{
    public bool BApply { get; private set; }
    public int TypeValue { get; private set; }
    public uint IncStatsKind { get; private set; }
    public uint DecStatsKind { get; private set; }
    public uint IncStatsPoint { get; private set; }
    public uint DecStatsPoint { get; private set; }
    public int PageIndex { get; private set; }

    public override void Read(PacketStream stream)
    {
        BApply = stream.ReadBoolean();
        TypeValue = stream.ReadInt32();
        IncStatsKind = stream.ReadUInt32();
        DecStatsKind = stream.ReadUInt32();
        IncStatsPoint = stream.ReadUInt32();
        DecStatsPoint = stream.ReadUInt32();
        PageIndex = stream.ReadInt32();
    }
}
