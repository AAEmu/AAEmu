using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Hands the client one craft order to add to its own list. Sent without a count — the packet carries
/// exactly one entry, unlike the load and search-answer packets.
/// </summary>
public class SCInsertCraftOrderEntryPacket(CraftOrderEntry entry) : GamePacket(SCOffsets.SCInsertCraftOrderEntryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return CraftOrderWire.WriteEntry(stream, entry);
    }
}
