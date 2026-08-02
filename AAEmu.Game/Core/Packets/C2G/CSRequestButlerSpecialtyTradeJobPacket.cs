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
public class CSRequestButlerSpecialtyTradeJobPacket() : GamePacket(CSOffsets.CSRequestButlerSpecialtyTradeJobPacket, 1)
{
    public sbyte Unnamed1 { get; private set; }
    public sbyte JobKind { get; private set; }
    public long DbSpecialtyTradeId { get; private set; }
    public int TypeValue { get; private set; }
    public short TypeValue2 { get; private set; }

    public override void Read(PacketStream stream)
    {
        Unnamed1 = stream.ReadSByte();
        JobKind = stream.ReadSByte();
        DbSpecialtyTradeId = stream.ReadInt64();
        TypeValue = stream.ReadInt32();
        TypeValue2 = stream.ReadInt16();
    }
}
