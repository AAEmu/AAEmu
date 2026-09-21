using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Hands the client the craft orders its own character has posted, as a count followed by that many
/// entries. The client keeps at most five, so a longer list is refused rather than truncated.
/// </summary>
public class SCLoadCraftOrderEntryPacket(IReadOnlyList<CraftOrderEntry> entries) : GamePacket(SCOffsets.SCLoadCraftOrderEntryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return CraftOrderWire.WriteEntries(stream, entries, CraftOrderWire.LoadEntryLimit);
    }
}
