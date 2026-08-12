using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One candidate on the ballot.</summary>
/// <param name="Unk0">Row field 0. Not read by X2Hero:GetCandidateList; meaning unknown, as in SCHeroList.</param>
/// <param name="CharId">Character id. The client resolves name and nameCacheQueryId from this.</param>
/// <param name="FactionId">The candidate's nation, at row +0x10 - the same slot SCHeroList keys its
/// faction map on.</param>
/// <param name="ExpeditionId">Guild, at row +0x14, resolved against the client's own expedition table.</param>
/// <param name="Ranking">Placing in the leadership ladder the candidates were drawn from.</param>
/// <param name="Score">Leadership earned this period.</param>
/// <param name="AccumPoint">Lifetime leadership. The client calls this one "leadership".</param>
/// <param name="VoteCount">Votes received. Read off the wire but never shown - the ballot does not
/// expose a live tally.</param>
/// <param name="Reputation">Peer-rating standing. Stored as an int and only converted to a float when
/// pushed to Lua (cvtdq2ps at .text 0x19e9a1), so it is an i32 on the wire.</param>
public readonly record struct HeroCandidateEntry(
    int Unk0,
    ulong CharId,
    int FactionId,
    int ExpeditionId,
    int Ranking,
    int Score,
    int AccumPoint,
    int VoteCount,
    int Reputation);

/// <summary>
/// The ballot - the candidates shown in the Hero Vote window.
/// </summary>
/// <remarks>
/// Layout disassembled from the client's reader at .text 0xaa3fc0:
///
///   showUI  -> +0x10  vtable +0xf8  bool
///   type    -> +0x14  vtable +0x80  i32
///   type    -> +0x18  vtable +0x80  i32
///   count   -> +0x1c  vtable +0x80  u32
///   rows    -> +0x20, stride 48, each read by 0xa3c970
///
/// Row order and widths, cross-checked against X2Hero:GetCandidateList (.text 0x19e620), which walks
/// the rows with a cursor at row+8 and a stride of 0x30 - so its reads translate back by subtracting 8:
///
///   +0x00  type        i32          (unread by the binding)
///   +0x08  charId      u64    <- [r13]      name, nameCacheQueryId
///   +0x10  type        i32          (the nation, by the same placement SCHeroList uses)
///   +0x14  expedition  i32    <- [r13+0x0c]
///   +0x18  ranking     i32    <- [r13+0x10] "rank"
///   +0x1c  score       i32    <- [r13+0x14]
///   +0x20  accumPoint  i32    <- [r13+0x18] "leadership"
///   +0x24  voteCount   i32          (unread by the binding)
///   +0x28  reputation  i32    <- [r13+0x20]
///
/// The 48-byte stride is in-memory alignment; the wire row is the 40 bytes actually read.
///
/// showUI is what opens the window. SCHeroVoting can raise HERO_ELECTION too - its handler does so when
/// bit 2 of voteInfo is set (.text 0x1085b2) - but this packet carries the contents, so opening from
/// here is the ordering that cannot show an empty ballot.
///
/// The two header i32s are the one part not pinned down. Nation then season is the reading that matches
/// the other hero packets, and it is what goes out; if the window comes up empty they are the first
/// thing to swap. X2Hero:GetCandidateList takes no faction argument - unlike GetHeroList and
/// GetRankingData - so the client keeps a single list and these are informational rather than a key.
/// </remarks>
public class SCHeroCandidateListPacket(
    bool showUi,
    int factionId,
    int season,
    IReadOnlyList<HeroCandidateEntry> entries = null)
    : GamePacket(SCOffsets.SCHeroCandidateListPacket, 1)
{
    /// <summary>
    /// hero_conditions.hero_candidate_scope is 16 in shipped data. This is a sanity bound: unlike the
    /// ranking list, the client sizes this array from the packet rather than holding a fixed buffer.
    /// </summary>
    public const int MaxEntries = 64;

    public override PacketStream Write(PacketStream stream)
    {
        var rows = entries ?? [];
        var count = Math.Min(rows.Count, MaxEntries);

        stream.Write(showUi);
        stream.Write(factionId);
        stream.Write(season);
        stream.Write((uint)count);

        for (var i = 0; i < count; i++)
        {
            var e = rows[i];
            stream.Write(e.Unk0);
            stream.Write(e.CharId);
            stream.Write(e.FactionId);
            stream.Write(e.ExpeditionId);
            stream.Write(e.Ranking);
            stream.Write(e.Score);
            stream.Write(e.AccumPoint);
            stream.Write(e.VoteCount);
            stream.Write(e.Reputation);
        }

        return stream;
    }
}
