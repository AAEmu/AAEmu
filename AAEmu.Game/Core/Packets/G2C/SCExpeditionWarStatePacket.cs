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
public class SCExpeditionWarStatePacket(int @type, int @type2, bool start, long protectDate, bool terminated) : GamePacket(SCOffsets.SCExpeditionWarStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(start);
        stream.Write(protectDate);
        stream.Write(terminated);
        return stream;
    }
}
