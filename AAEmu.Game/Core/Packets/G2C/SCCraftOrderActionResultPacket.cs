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
public class SCCraftOrderActionResultPacket(sbyte unnamed1, sbyte kind, bool result) : GamePacket(SCOffsets.SCCraftOrderActionResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(unnamed1);
        stream.Write(kind);
        stream.Write(result);
        return stream;
    }
}
