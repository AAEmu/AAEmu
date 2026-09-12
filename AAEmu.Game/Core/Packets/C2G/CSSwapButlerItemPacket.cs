using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO: the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's serializer. The location fields remain
/// raw bytes until their domain values are verified.
/// </remarks>
public class CSSwapButlerItemPacket() : GamePacket(CSOffsets.CSSwapButlerItemPacket, 1)
{
    private const int BodySize = sizeof(byte) * 4 + sizeof(ulong);

    public byte FromType { get; private set; }
    public byte FromIndex { get; private set; }
    public byte ToType { get; private set; }
    public byte ToIndex { get; private set; }
    public ulong ButlerItemId { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler item-swap body, got {stream.LeftBytes} bytes.");

        FromType = stream.ReadByte();
        FromIndex = stream.ReadByte();
        ToType = stream.ReadByte();
        ToIndex = stream.ReadByte();
        ButlerItemId = stream.ReadUInt64();
    }
}
