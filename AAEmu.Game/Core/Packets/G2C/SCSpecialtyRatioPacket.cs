using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A page of the specialty cargo buy list. Native opcode 0xC5 names this packet "Ratio", but the
/// 10.0.2.13 body is a keyed, paged quote list rather than the scalar used by the 1.2 client.
/// </summary>
public class SCSpecialtyRatioPacket(
    ushort zoneGroupId,
    uint npcTemplateId,
    IReadOnlyList<SpecialtyQuote> quotes,
    IReadOnlyList<uint> eventIds,
    bool isBegin,
    bool isEnd) : GamePacket(SCOffsets.SCSpecialtyRatioPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (quotes.Count > 20)
            throw new ArgumentOutOfRangeException(nameof(quotes), "A specialty buy-list page can contain at most 20 quotes.");
        if (eventIds.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(eventIds), "A specialty buy-list page can contain at most 50 events.");

        stream.Write(zoneGroupId);
        stream.Write(npcTemplateId);
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
