using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One board of the rankings: the lines it holds, then the tiers its places are grouped into.
/// </summary>
/// <remarks>
/// Field order and widths come from the 10.0.2.13 client's serializer, which passes each value's name
/// alongside the value: the requested type and division, a line count and the lines, a tier count and
/// the tiers, then the board's own id and when the board was taken. The client caps both lists, so the
/// counts are capped here too — a longer list would leave it reading past what we wrote. The tiers are
/// left empty: the tables' tiers are the reward bands of a board rather than holders of a place, and the
/// client draws whatever this list carries as lines.
/// </remarks>
public class SCRankSnapshotPacket(
    uint type,
    uint divisionId,
    IReadOnlyList<RankingOrderedEntry> entries,
    long boardId,
    ulong snappedTime)
    : GamePacket(SCOffsets.SCRankSnapshotPacket, 1)
{
    /// <summary>The most lines the client reads from a board.</summary>
    public const int MaxEntries = 100;

    /// <summary>The most tiers the client reads from a board.</summary>
    public const int MaxScopes = 20;

    public override PacketStream Write(PacketStream stream)
    {
        WriteBoard(stream, type, divisionId, entries, boardId);

        // The board's own answer ends with when the board was taken; the reward answer carries no time
        // (the window reads its season's off date itself).
        stream.Write(snappedTime);
        return stream;
    }

    /// <summary>
    /// The part a board's answer shares with the reward answer: the requested type and division, a line
    /// count and the lines, a tier count and the tiers, then the board's own id.
    /// </summary>
    /// <remarks>
    /// The client reads exactly this much and stops, which a live capture of both answers shows: 79 bytes
    /// of body whatever follows. The tiers are left empty — the tables' tiers are the reward bands of a
    /// board rather than holders of a place, and the client draws whatever this list carries as lines.
    /// </remarks>
    internal static void WriteBoard(PacketStream stream, uint type, uint divisionId,
        IReadOnlyList<RankingOrderedEntry> entries, long boardId)
    {
        stream.Write(type);
        stream.Write(divisionId);

        var entryCount = Math.Min(entries?.Count ?? 0, MaxEntries);
        stream.Write((uint)entryCount);
        for (var i = 0; i < entryCount; i++)
            entries[i].Write(stream);

        stream.Write(0u); // no tiers: see the remarks

        stream.Write(boardId);
    }
}
