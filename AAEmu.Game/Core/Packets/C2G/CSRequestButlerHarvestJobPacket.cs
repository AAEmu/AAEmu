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
    private const int BodySize = sizeof(sbyte) + sizeof(long) + sizeof(int) + sizeof(short);

    public sbyte JobKind { get; private set; }
    public long DbHarvestId { get; private set; }
    public int HarvestId { get; private set; }
    public short Amount { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler harvest-job body, got {stream.LeftBytes} bytes.");

        JobKind = stream.ReadSByte();
        DbHarvestId = stream.ReadInt64();
        HarvestId = stream.ReadInt32();
        Amount = stream.ReadInt16();
    }
}
