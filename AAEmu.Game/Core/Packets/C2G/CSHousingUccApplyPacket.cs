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
public class CSHousingUccApplyPacket() : GamePacket(CSOffsets.CSHousingUccApplyPacket, 1)
{
    public long ItemId { get; private set; }
    public sbyte TypeValue { get; private set; }
    public sbyte Index { get; private set; }
    public short Tl { get; private set; }
    public uint Pos { get; private set; }
    public bool IsRemove { get; private set; }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadInt64();
        TypeValue = stream.ReadSByte();
        Index = stream.ReadSByte();
        Tl = stream.ReadInt16();
        Pos = stream.ReadUInt32();
        IsRemove = stream.ReadBoolean();
    }
}
