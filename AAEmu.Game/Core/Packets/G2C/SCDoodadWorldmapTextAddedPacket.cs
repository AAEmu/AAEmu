using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCDoodadWorldmapTextAddedPacket(short @type, uint bc, int @type2, int timeover) : GamePacket(SCOffsets.SCDoodadWorldmapTextAddedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.WriteBc(bc);
        stream.Write(@type2);
        stream.Write(timeover);
        return stream;
    }
}
