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
    private const int BodySize = sizeof(sbyte) + sizeof(long) + sizeof(int) + sizeof(short);

    public sbyte JobKind { get; private set; }
    public long DbSpecialtyTradeId { get; private set; }
    public int SpecialtyTradeType { get; private set; }
    public short ToZoneGroupType { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler specialty-job body, got {stream.LeftBytes} bytes.");

        JobKind = stream.ReadSByte();
        DbSpecialtyTradeId = stream.ReadInt64();
        SpecialtyTradeType = stream.ReadInt32();
        ToZoneGroupType = stream.ReadInt16();
    }
}
