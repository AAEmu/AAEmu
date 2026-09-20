using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client one of its craft orders was filled. Carries the same entry shape as the insert
/// packet, without a count, so the client can refresh the row before the order is removed.
/// </summary>
public class SCCompleteCraftOrderEntryPacket(CraftOrderEntry entry) : GamePacket(SCOffsets.SCCompleteCraftOrderEntryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return CraftOrderWire.WriteEntry(stream, entry);
    }
}
