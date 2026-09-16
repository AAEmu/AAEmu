using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>
/// One line of a ranking as the client reads it: the values the ranking is scored on, when they were
/// recorded, and who holds them. The field order and widths are the client's own.
/// </summary>
public class RankingEntry
{
    /// <summary>The ranking's primary value — a gear score, a fish length, a battlefield score.</summary>
    public long V1 { get; set; }

    /// <summary>The secondary value the ranking shows beside it.</summary>
    public long V2 { get; set; }

    /// <summary>When the value was recorded, in the client's own time base (unix seconds).</summary>
    public long Timestamp { get; set; }

    /// <summary>The game world the holder is on.</summary>
    public byte WorldId { get; set; }

    /// <summary>Character id of the holder.</summary>
    public ulong Id { get; set; }

    public ulong AccountId { get; set; }

    /// <summary>The ranking this line belongs to.</summary>
    public long Type { get; set; }

    public byte PrivacyStatus { get; set; }

    public virtual void Write(PacketStream stream)
    {
        stream.Write(V1);
        stream.Write(V2);
        stream.Write(Timestamp);
        stream.Write(WorldId);
        stream.Write(Id);
        stream.Write(AccountId);
        stream.Write(Type);
        stream.Write(PrivacyStatus);

        // The client reads a flag here and, when it is set, an optional sub-data block: a kind byte plus a
        // fixed-size detail whose length depends on that kind. This server has no sub-data to send, so the
        // flag stays clear — raising it without the block would leave the client reading the next line's
        // bytes as the detail.
        stream.Write(false);
    }
}

/// <summary>
/// A line of a board, which carries where it stands in addition to the entry itself.
/// </summary>
/// <remarks>The client reads a board's lines with the standing last, after the entry's own fields.</remarks>
public class RankingOrderedEntry : RankingEntry
{
    /// <summary>The line's place in its board, 1 for the top of it; 0 when the board has no place to give.</summary>
    public uint Ranking { get; set; }

    public override void Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Ranking);
    }
}

/// <summary>One ranking's line for a character: the key the client files it under and the line itself.</summary>
public readonly record struct RankingEntryLine(uint Key, RankingEntry Entry);
