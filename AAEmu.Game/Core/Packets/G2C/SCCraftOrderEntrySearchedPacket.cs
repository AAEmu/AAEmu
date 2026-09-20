using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answers a craft-order search: the total number of matching orders, the page it is answering for,
/// then that page's entries. The client pages from <paramref name="totalCount"/>, so it has to be the
/// full match count and not the number of rows written.
/// </summary>
public class SCCraftOrderEntrySearchedPacket(uint totalCount, uint page, IReadOnlyList<CraftOrderEntry> entries)
    : GamePacket(SCOffsets.SCCraftOrderEntrySearchedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(totalCount);
        stream.Write(page);
        return CraftOrderWire.WriteEntries(stream, entries, CraftOrderWire.SearchEntryLimit);
    }
}
