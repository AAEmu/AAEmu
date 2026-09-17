namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// The plot timeline: what to run next and when it is due.
/// </summary>
/// <remarks>
/// The tree used to hold its pending nodes in a <c>Queue</c>, dequeue one per pass, and put it back at the
/// back when it was not due yet after sleeping a flat 15 ms. With N pending nodes that resolves a due time
/// to about N x 15 ms, and the order nodes actually ran in was arrival order rather than due order. A plot
/// with a 1,500 ms cast edge next to a 0 ms one therefore ran the 0 ms edge up to N x 15 ms late, and the
/// whole zero-delay chain of a gun plot (5796/5604) ran behind whatever had been queued first.
///
/// Entries are ordered by due time, earliest first, with enqueue order breaking ties, so a pass only ever
/// touches the node that is actually next.
/// </remarks>
public sealed class PlotSchedule<T>
{
    /// <summary>
    /// Longest single sleep. The wait for a channel edge can be minutes long and a bite or a client stop
    /// has to be seen inside it, so the wait to the earliest due time is taken in slices of this length.
    /// </summary>
    public const int PollIntervalMs = 15;

    private readonly PriorityQueue<(T Item, DateTime DueUtc), (DateTime DueUtc, long Sequence)> _queue = new();
    private long _sequence;

    public int Count => _queue.Count;

    /// <summary>Due time of the node that will run next, or null when nothing is pending.</summary>
    public DateTime? NextDueUtc =>
        _queue.TryPeek(out _, out var priority) ? priority.DueUtc : null;

    public void Enqueue(T item, DateTime dueUtc) =>
        _queue.Enqueue((item, dueUtc), (dueUtc, _sequence++));

    /// <summary>Removes the earliest node when it is due, and reports whether it was.</summary>
    public bool TryDequeueDue(DateTime nowUtc, out T item)
    {
        if (_queue.TryPeek(out _, out var priority) && priority.DueUtc <= nowUtc)
        {
            item = _queue.Dequeue().Item;
            return true;
        }

        item = default;
        return false;
    }

    /// <summary>Removes every node, earliest first, keeping each one's due time.</summary>
    public List<(T Item, DateTime DueUtc)> DrainAll()
    {
        var drained = new List<(T Item, DateTime DueUtc)>(_queue.Count);
        while (_queue.TryDequeue(out var entry, out _))
            drained.Add(entry);
        return drained;
    }

    /// <summary>
    /// How long to sleep before the next pass: never past the earliest due time, never longer than one
    /// poll interval, and 0 when something is already due so the caller keeps the thread.
    /// </summary>
    public static int WaitSliceMs(DateTime? nextDueUtc, DateTime nowUtc, int pollIntervalMs = PollIntervalMs)
    {
        if (!nextDueUtc.HasValue)
            return 0;

        var remainingMs = (nextDueUtc.Value - nowUtc).TotalMilliseconds;
        if (remainingMs <= 0)
            return 0;

        // Round up: sleeping a truncated millisecond less than the due time costs another whole pass.
        return Math.Min(pollIntervalMs, Math.Max(1, (int)Math.Ceiling(remainingMs)));
    }
}
