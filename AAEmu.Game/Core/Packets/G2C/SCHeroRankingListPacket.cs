using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One row of the leadership ranking.</summary>
/// <remarks>
/// The client resolves the displayed name from <paramref name="CharId"/> through its name cache - the
/// Lua row carries a nameCacheQueryId alongside the name - which is why no strings travel on the wire
/// and a row is a fixed 20 bytes. The guild does not come from the name cache; it is
/// <paramref name="ExpeditionId"/>, resolved against the client's own expedition table.
///
/// <paramref name="Leadership"/> is the LIFETIME total and <paramref name="Score"/> is what the
/// character earned in the current period; the client prints them as "score/leadership". The list is
/// ordered by Score, which is why retail rows look like 3398/621999 with the left column descending and
/// the right one in no order at all.
/// </remarks>
public readonly record struct HeroRankingEntry(ulong CharId, int Leadership, int Score, int ExpeditionId);

/// <summary>
/// Answers CSHeroRankingList - the leadership ranking behind the Hero window's "Candidates" tab.
/// </summary>
/// <remarks>
/// Layout disassembled from the client's reader at .text 0xc628a0, not guessed. Each field is read by
/// calling a virtual on the stream with its name, which is also where the client's "not enough buffer
/// for &lt;field&gt;" warnings come from:
///
///   type        -> struct +0x10   vtable +0x80   i32   (faction)
///   leadership  -> struct +0x14   vtable +0x80   i32   (VIEWER's own)
///   score       -> struct +0x18   vtable +0x80   i32   (VIEWER's own)
///   rankCount   -> struct +0x980  vtable +0x80   u32
///   then  cmp dword [rsi+0x980], 0 / jbe  - the row loop guard
///
/// Row loop at 0xc62940, indexing `lea rbx, [rsi + rcx*8 + 0x20]` with rcx = 3*i, so the rows live at
/// struct +0x20 with a 24-byte stride:
///
///   type        -> row +0x0   vtable +0x98   u64   (charId - the wider reader, next field is 8 on)
///   leadership  -> row +0x8   vtable +0x80   i32
///   score       -> row +0xc   vtable +0x80   i32
///   type        -> row +0x10  vtable +0x80   i32   (the EXPEDITION - see below)
///
/// The 24-byte stride is the in-memory array's alignment padding; the wire row is the 20 bytes
/// actually read.
///
/// Row +0x10 is the expedition. X2Hero:GetRankingData (.text 0x19bde0) walks the rows with its cursor
/// in rsi and pushes score from [rsi+0x0c], then compares [rsi+0x10] against the invalid-id sentinel
/// (0x19c0ad) and only emits the "expedition" key when they differ - the same guard the hero list uses.
/// The column is real: hero_rank.lua:45 adds GetExpeidtionColumnInfoRelatedHero (the client's own
/// misspelling), which reads data.expedition.
///
/// leadership and score in the HEADER are the viewer's own figures, feeding the "Current Record" line
/// above the table - not a row. leadership is the LIFETIME total, score is the current period, and the
/// client renders the pair as "period/lifetime".
/// </remarks>
public class SCHeroRankingListPacket(
    int factionId,
    int viewerLeadership,   // lifetime
    int viewerScore,        // current period
    IReadOnlyList<HeroRankingEntry> entries = null)
    : GamePacket(SCOffsets.SCHeroRankingListPacket, 1)
{
    /// <summary>
    /// Rows the client can physically hold: its array runs from struct +0x20 to rankCount at +0x980,
    /// at a 24-byte stride. Overrunning it writes past the packet struct in the client's own memory, so
    /// this is a hard cap rather than a tidy limit. hero_conditions.leadership_ranking_scope (50) keeps
    /// real traffic well under it.
    /// </summary>
    public const int MaxEntries = (0x980 - 0x20) / 24;

    public override PacketStream Write(PacketStream stream)
    {
        var rows = entries ?? [];
        var count = Math.Min(rows.Count, MaxEntries);

        stream.Write(factionId);        // type
        stream.Write(viewerLeadership); // leadership - viewer's own
        stream.Write(viewerScore);      // score      - viewer's own
        stream.Write((uint)count);      // rankCount

        for (var i = 0; i < count; i++)
        {
            var e = rows[i];
            stream.Write(e.CharId);     // type (u64 charId)
            stream.Write(e.Leadership);
            stream.Write(e.Score);
            stream.Write(e.ExpeditionId);
        }

        return stream;
    }
}
