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

    public DateTime PeriodStartUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One holder's place on a board.</summary>
public readonly record struct RankPlace(RankScore Score, uint Position);
