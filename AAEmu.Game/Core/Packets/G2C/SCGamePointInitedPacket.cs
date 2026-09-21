using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// The 10.0.2.13 client reads this as a u8 kind followed by a u32 point - five bytes. It used to
/// write an extra leading sbyte, so the client would have taken the kind from the wrong byte and the
/// point from the wrong offset had anything ever sent it. Widths come from the client's serializer,
/// which passes each value's name alongside the value: kind, then point.
/// </remarks>
public class SCGamePointInitedPacket(sbyte kind, uint point) : GamePacket(SCOffsets.SCGamePointInitedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(kind);
        stream.Write(point);
        return stream;
    }
}
