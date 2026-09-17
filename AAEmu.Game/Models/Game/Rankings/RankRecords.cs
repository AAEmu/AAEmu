namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>
/// The figures a board ranks that the World meets while a character plays, rather than reads off live
/// state: what a character caught, and what they handed in.
/// </summary>
public enum RankRecordKind : byte
{
    /// <summary>How long the longest fish caught in the window was, in the units the item carries.</summary>
    FishLength = 1,

    /// <summary>What every fish caught in the window weighed together.</summary>
    FishWeight = 2
}

/// <summary>How a record's figure is kept over a window.</summary>
public enum RankRecordAggregate : byte
{
    /// <summary>The highest figure of the window stands — "the longest fish".</summary>
    Best = 0,

    /// <summary>Every figure of the window adds up — "what was caught in total".</summary>
    Total = 1
}

/// <summary>
/// Which board ranks which record. A board kind the World does not meet in play has no record, and its
/// board stays empty rather than being given a figure nothing produced.
/// </summary>
public static class RankRecordRules
{
    /// <summary>The record a board kind ranks, or null when the World produces no figure for it.</summary>
    public static RankRecordKind? ForBoardKind(uint rankKindId)
    {
        return rankKindId switch
        {
            // fishing_sum: everything caught in the window weighs in
            3 => RankRecordKind.FishWeight,

            // fishing_top: only the longest single catch of the window stands
            4 => RankRecordKind.FishLength,

            _ => null
        };
    }

    /// <summary>How a record's figure is kept over a window.</summary>
    public static RankRecordAggregate AggregateOf(RankRecordKind kind)
    {
        return kind switch
        {
            RankRecordKind.FishLength => RankRecordAggregate.Best,
            _ => RankRecordAggregate.Total
        };
    }
}

/// <summary>One thing worth ranking that a character did, before it is written.</summary>
public readonly record struct RankRecordEvent(RankRecordKind Kind, long Value, DateTime RecordedAtUtc);

/// <summary>
/// A character's records for the boards that rank what they did, kept while they play and written with
/// them — the same way their running point totals are.
/// </summary>
public class RankRecords
{
    private readonly List<RankRecordEvent> _pending = [];

    /// <summary>Records one thing a character did. A figure of nothing changes nothing.</summary>
    public void Add(RankRecordKind kind, long value, DateTime recordedAtUtc)
    {
        if (value <= 0)
            return;

        _pending.Add(new RankRecordEvent(kind, value, recordedAtUtc));
    }

    /// <summary>Whether anything has been recorded since the last write.</summary>
    public bool HasPending => _pending.Count > 0;

    /// <summary>What is waiting to be written, oldest first.</summary>
    public IReadOnlyList<RankRecordEvent> Pending => _pending;

    /// <summary>Forgets what has been written.</summary>
    public void Clear() => _pending.Clear();
}
