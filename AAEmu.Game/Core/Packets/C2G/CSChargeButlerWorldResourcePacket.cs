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
public class CSChargeButlerWorldResourcePacket() : GamePacket(CSOffsets.CSChargeButlerWorldResourcePacket, 1)
{
    private const int BodySize = sizeof(sbyte) + sizeof(uint);

    public sbyte ChargeKind { get; private set; }
    public uint Amount { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte butler resource-charge body, got {stream.LeftBytes} bytes.");

        ChargeKind = stream.ReadSByte();
        Amount = stream.ReadUInt32();
    }
}
