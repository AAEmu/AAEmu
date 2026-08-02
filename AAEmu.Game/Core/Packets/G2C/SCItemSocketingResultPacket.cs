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
public class SCItemSocketingResultPacket(sbyte unnamed1, sbyte result, long itemId, int @type, sbyte unnamed2, bool success) : GamePacket(SCOffsets.SCItemSocketingResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(unnamed1);
        stream.Write(result);
        stream.Write(itemId);
        stream.Write(@type);
        stream.Write(unnamed2);
        stream.Write(success);
        return stream;
    }
}
