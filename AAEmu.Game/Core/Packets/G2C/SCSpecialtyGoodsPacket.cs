using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A page of specialty packs accepted by a trade outlet.
/// </summary>
public class SCSpecialtyGoodsPacket(
    IReadOnlyList<SpecialtyQuote> quotes,
    IReadOnlyList<uint> eventIds,
    bool isBegin,
    bool isEnd) : GamePacket(SCOffsets.SCSpecialtyGoodsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (quotes.Count > 20)
            throw new ArgumentOutOfRangeException(nameof(quotes), "A specialty sell-list page can contain at most 20 quotes.");
        if (eventIds.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(eventIds), "A specialty sell-list page can contain at most 50 events.");

        stream.Write((uint)quotes.Count);
        stream.Write((uint)eventIds.Count);
        stream.Write(isBegin);
        stream.Write(isEnd);
        foreach (var quote in quotes)
            stream.Write(quote);
        foreach (var eventId in eventIds)
            stream.Write(eventId);
        return stream;
    }
}
