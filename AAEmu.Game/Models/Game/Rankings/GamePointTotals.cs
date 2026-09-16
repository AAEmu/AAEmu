namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>
/// The figures a period board ranks, as <c>game_point_rank_details</c> names them: which of a character's
/// running totals a board counts, and whether it counts what they gained or what they spent.
/// </summary>
public static class RankGamePoints
{
    public const int Experience = 0;
    public const int Honor = 1;
    public const int LivingPoint = 2;
    public const int Labor = 3;

    public const int Gained = 0;
    public const int Spent = 1;
}

/// <summary>
/// A character's running totals for the boards that rank a period, kept while they play and written with
/// them. Each entry is the amount gained or spent since the last write.
/// </summary>
public class GamePointTotals
{
    private readonly Dictionary<(int Kind, int Method), long> _pending = [];

    /// <summary>Adds an amount to a running total. A zero amount changes nothing.</summary>
    public void Add(int kind, int method, long amount)
    {
        if (amount == 0)
            return;

        var key = (kind, method);
        _pending[key] = _pending.TryGetValue(key, out var current) ? current + amount : amount;
    }

    /// <summary>Whether anything has been gained or spent since the last write.</summary>
    public bool HasPending => _pending.Count > 0;

    /// <summary>What is waiting to be written, per running total.</summary>
    public IReadOnlyDictionary<(int Kind, int Method), long> Pending => _pending;

    /// <summary>Forgets what has been written.</summary>
    public void Clear() => _pending.Clear();
}
