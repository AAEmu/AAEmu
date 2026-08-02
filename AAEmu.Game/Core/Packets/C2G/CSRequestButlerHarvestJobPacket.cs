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
public class CSRequestButlerHarvestJobPacket() : GamePacket(CSOffsets.CSRequestButlerHarvestJobPacket, 1)
{
    public sbyte Unnamed1 { get; private set; }
    public sbyte JobKind { get; private set; }
    public long DbHarvestId { get; private set; }
    public int TypeValue { get; private set; }
    public short Amount { get; private set; }

    public override void Read(PacketStream stream)
    {
        Unnamed1 = stream.ReadSByte();
        JobKind = stream.ReadSByte();
        DbHarvestId = stream.ReadInt64();
        TypeValue = stream.ReadInt32();
        Amount = stream.ReadInt16();
    }
}
