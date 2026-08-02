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
public class SCChronicleInfoUpdatePacket(sbyte prevStatus, sbyte curStatus, int @type) : GamePacket(SCOffsets.SCChronicleInfoUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(prevStatus);
        stream.Write(curStatus);
        stream.Write(@type);
        return stream;
    }
}
