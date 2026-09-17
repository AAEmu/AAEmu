namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>What a board counts: a character, or the expedition they belong to.</summary>
public enum RankHolderKind : byte
{
    Character = 0,
    Expedition = 1
}

/// <summary>One holder's value on one board, in one of its windows.</summary>
public class RankScore
{
    public uint RankId { get; set; }
    public RankHolderKind HolderKind { get; set; }
    public ulong HolderId { get; set; }
    public uint AccountId { get; set; }
    public byte WorldId { get; set; }

    /// <summary>The value the board orders by.</summary>
    public long Value { get; set; }

    /// <summary>The figure the window shows beside it, when a board carries one.</summary>
    public long BareValue { get; set; }

    /// <summary>
    /// The line's own detail block as the client reads it, or null when the board's lines carry none. A
    /// board over equipped pieces keeps the item's template id here, and a board over expeditions the
    /// counts it shows.
    /// </summary>
    public byte[] SubData { get; set; }

    public DateTime PeriodStartUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One holder's place on a board.</summary>
public readonly record struct RankPlace(RankScore Score, uint Position);

/// <summary>
/// What a holder is worth on one board: the figure the board orders by, the figure the window shows
/// beside it, and the line's own detail when the board's lines carry one.
/// </summary>
public readonly record struct RankLine(long Value, long BareValue, RankingSubData SubData);
