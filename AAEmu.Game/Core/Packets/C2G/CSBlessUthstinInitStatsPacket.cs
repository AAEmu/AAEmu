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
public class CSBlessUthstinInitStatsPacket() : GamePacket(CSOffsets.CSBlessUthstinInitStatsPacket, 1)
{
    public int UthstinPageIndex { get; private set; }
    public uint ChangeStat { get; private set; }
    public uint Stats { get; private set; }

    public override void Read(PacketStream stream)
    {
        UthstinPageIndex = stream.ReadInt32();
        ChangeStat = stream.ReadUInt32();
        Stats = stream.ReadUInt32();
    }
}
