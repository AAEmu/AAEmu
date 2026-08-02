using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// </remarks>
public class CSReopenRandomBoxGetItemPacket() : GamePacket(CSOffsets.CSReopenRandomBoxGetItemPacket, 1)
{
    public int Type1 { get; private set; }
    public int Type2 { get; private set; }
    public int Type3 { get; private set; }
    public int Type4 { get; private set; }
    public uint FreeCnt { get; private set; }
    public uint ChargeCnt { get; private set; }
    public uint LifeTime { get; private set; }
    public long ItemId { get; private set; }
    public long OpenDate { get; private set; }
    public long RefreshDate { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type1 = stream.ReadInt32();
        Type2 = stream.ReadInt32();
        Type3 = stream.ReadInt32();
        Type4 = stream.ReadInt32();
        FreeCnt = stream.ReadUInt32();
        ChargeCnt = stream.ReadUInt32();
        LifeTime = stream.ReadUInt32();
        ItemId = stream.ReadInt64();
        OpenDate = stream.ReadInt64();
        RefreshDate = stream.ReadInt64();
    }
}
