using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Removes one craft order from the client's list. <paramref name="type"/> is the order id;
/// <paramref name="complete"/> is true when the row was filled rather than cancelled.
/// </summary>
public class SCDeleteCraftOrderEntryPacket(ulong @type, bool complete) : GamePacket(SCOffsets.SCDeleteCraftOrderEntryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(complete);
        return stream;
    }
}
