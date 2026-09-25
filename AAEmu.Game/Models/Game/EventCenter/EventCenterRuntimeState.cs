namespace AAEmu.Game.Models.Game.EventCenter;

/// <summary>Where a projected schedule row sits in the board's runtime view.</summary>
public enum EventCenterRuntimeRowState
{
    /// <summary>The row has no single runtime period that this slice can classify.</summary>
    Deferred,

    /// <summary>The row is the next future period, sharing the earliest future start.</summary>
    Upcoming,

    /// <summary>The row's period contains the evaluation instant.</summary>
    Current,

    /// <summary>The row's period ended at or before the evaluation instant.</summary>
    Ended
}

/// <summary>Why a row was kept out of the current/upcoming runtime view.</summary>
public enum EventCenterRuntimeDeferReason
{
    /// <summary>The row is classified.</summary>
    None,

    /// <summary>The projected period did not resolve to two positive-length instants.</summary>
    PeriodUnresolved,

    /// <summary>
    /// The row repeats inside its period. Expanding one row into occurrences, or listing the row once,
    /// is a product decision that this slice deliberately does not make.
    /// </summary>
    RecurringOccurrence,

    /// <summary>The projected schedule shape is malformed for runtime use.</summary>
    InvalidShape
}

/// <summary>One projected row with the runtime classification made from its own bounds.</summary>
public sealed record EventCenterRuntimeRow
{
    /// <summary>The W09A projection this classification came from.</summary>
    public EventCenterRowProjection Projection { get; init; }

    /// <summary>Runtime classification at the snapshot's evaluation instant.</summary>
    public EventCenterRuntimeRowState State { get; init; }

    /// <summary>Why the row is deferred, when it is.</summary>
    public EventCenterRuntimeDeferReason DeferReason { get; init; }

    /// <summary>Convenience accessor for the projected schedule id.</summary>
    public int ScheduleId => Projection?.ScheduleId ?? throw new InvalidOperationException(
        "An event-center runtime row must carry its W09A projection.");

    /// <summary>Convenience accessor for the projected period start.</summary>
    public DateTimeOffset? StartUtc => Projection.PeriodStart;

    /// <summary>Convenience accessor for the projected period end.</summary>
    public DateTimeOffset? EndUtc => Projection.PeriodEnd;
}

/// <summary>An immutable board runtime view evaluated at one wall-clock instant.</summary>
public sealed record EventCenterRuntimeSnapshot
{
    /// <summary>A snapshot used before the runtime has loaded; it has no clock instant.</summary>
    public static EventCenterRuntimeSnapshot Empty { get; } = new()
    {
        EvaluatedAtUtc = null,
        Rows = [],
        CurrentScheduleIds = [],
        UpcomingScheduleIds = [],
        DeferredScheduleIds = [],
        EndedScheduleIds = [],
        NextBoundaryUtc = null
    };

    /// <summary>The UTC instant used for every classification in this snapshot.</summary>
    public DateTimeOffset? EvaluatedAtUtc { get; init; }

    /// <summary>Every projected row, in stable schedule-id order.</summary>
    public IReadOnlyList<EventCenterRuntimeRow> Rows { get; init; } = [];

    /// <summary>Rows whose half-open period contains <see cref="EvaluatedAtUtc"/>.</summary>
    public IReadOnlyList<int> CurrentScheduleIds { get; init; } = [];

    /// <summary>Rows sharing the earliest future period start.</summary>
    public IReadOnlyList<int> UpcomingScheduleIds { get; init; } = [];

    /// <summary>Rows whose shape does not yield one unambiguous runtime period.</summary>
    public IReadOnlyList<int> DeferredScheduleIds { get; init; } = [];

    /// <summary>Rows whose period has ended.</summary>
    public IReadOnlyList<int> EndedScheduleIds { get; init; } = [];

    /// <summary>
    /// The next instant at which a current row ends or an upcoming row starts. The value is selected
    /// only from projected row bounds; there is no polling or look-ahead constant.
    /// </summary>
    public DateTimeOffset? NextBoundaryUtc { get; init; }

    /// <summary>How many projected rows could ever be written to the board, as reported by W09A.</summary>
    public int WireReadyRows => Rows.Count(row => row.Projection?.IsWireReady == true);
}

/// <summary>The result of initializing or refreshing the runtime state machine.</summary>
public sealed record EventCenterRuntimeUpdate
{
    /// <summary>The state after the evaluation.</summary>
    public EventCenterRuntimeSnapshot Snapshot { get; init; } = EventCenterRuntimeSnapshot.Empty;

    /// <summary>Rows that became current at this evaluation.</summary>
    public IReadOnlyList<int> StartedScheduleIds { get; init; } = [];

    /// <summary>Rows that were current before and are no longer current now.</summary>
    public IReadOnlyList<int> EndedScheduleIds { get; init; } = [];

    /// <summary>True when the current set changed.</summary>
    public bool CurrentChanged { get; init; }

    /// <summary>True when the next-upcoming set changed.</summary>
    public bool UpcomingChanged { get; init; }

    /// <summary>True when the next projected boundary changed.</summary>
    public bool BoundaryChanged { get; init; }

    /// <summary>True when this was a fresh boot/restart reconstruction rather than a refresh.</summary>
    public bool IsReplay { get; init; }
}

/// <summary>Deterministic current/upcoming selection over W09A projections.</summary>
public static class EventCenterRuntimeEvaluator
{
    /// <summary>
    /// Classifies rows at <paramref name="now"/>. A resolved period is half-open: its start is current,
    /// its end is not. Rows with a recurring or unresolved shape are deferred rather than expanded.
    /// </summary>
    public static EventCenterRuntimeSnapshot Evaluate(
        IEnumerable<EventCenterRowProjection> projections,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(projections);

        var utcNow = now.ToUniversalTime();
        var ordered = projections.ToList();
        if (ordered.Any(projection => projection == null))
        {
            throw new InvalidOperationException(
                "Event-center runtime cannot classify a null schedule projection.");
        }

        ordered = ordered
            .OrderBy(projection => projection.ScheduleId)
            .ToList();

        var seen = new HashSet<int>();
        var runtimeRows = new List<EventCenterRuntimeRow>(ordered.Count);
        var current = new List<EventCenterRuntimeRow>();
        var future = new List<EventCenterRuntimeRow>();
        var deferred = new List<EventCenterRuntimeRow>();
        var ended = new List<EventCenterRuntimeRow>();

        foreach (var projection in ordered)
        {
            if (!seen.Add(projection.ScheduleId))
            {
                throw new InvalidOperationException(
                    $"Two event-center projections share schedule id {projection.ScheduleId}.");
            }

            if (!projection.IsPeriodResolved)
            {
                var unresolved = Deferred(projection, EventCenterRuntimeDeferReason.PeriodUnresolved);
                runtimeRows.Add(unresolved);
                deferred.Add(unresolved);
                continue;
            }

            if (projection.ShapeGaps != EventCenterRowGap.None)
            {
                var reason = (projection.ShapeGaps
                              & (EventCenterRowGap.RecurringWithinPeriod
                                 | EventCenterRowGap.RecurringByWeekday)) != 0
                    ? EventCenterRuntimeDeferReason.RecurringOccurrence
                    : EventCenterRuntimeDeferReason.InvalidShape;
                var shapeDeferred = Deferred(projection, reason);
                runtimeRows.Add(shapeDeferred);
                deferred.Add(shapeDeferred);
                continue;
            }

            var start = projection.PeriodStart.Value;
            var end = projection.PeriodEnd.Value;
            if (end <= start)
            {
                throw new InvalidOperationException(
                    $"Event-center schedule {projection.ScheduleId} has a resolved period that is not positive-length.");
            }

            if (utcNow < start)
            {
                var upcoming = new EventCenterRuntimeRow
                {
                    Projection = projection,
                    State = EventCenterRuntimeRowState.Upcoming,
                    DeferReason = EventCenterRuntimeDeferReason.None
                };
                runtimeRows.Add(upcoming);
                future.Add(upcoming);
            }
            else if (utcNow < end)
            {
                var currentRow = new EventCenterRuntimeRow
                {
                    Projection = projection,
                    State = EventCenterRuntimeRowState.Current,
                    DeferReason = EventCenterRuntimeDeferReason.None
                };
                runtimeRows.Add(currentRow);
                current.Add(currentRow);
            }
            else
            {
                var endedRow = new EventCenterRuntimeRow
                {
                    Projection = projection,
                    State = EventCenterRuntimeRowState.Ended,
                    DeferReason = EventCenterRuntimeDeferReason.None
                };
                runtimeRows.Add(endedRow);
                ended.Add(endedRow);
            }
        }

        var earliestFuture = future.Count == 0
            ? (DateTimeOffset?)null
            : future.Min(row => row.StartUtc!.Value);
        var nextUpcoming = earliestFuture.HasValue
            ? future
                .Where(row => row.StartUtc!.Value == earliestFuture.Value)
                .OrderBy(row => row.ScheduleId)
                .Select(row => row.ScheduleId)
                .ToArray()
            : [];
        var nextCurrentEnd = current.Count == 0
            ? (DateTimeOffset?)null
            : current.Min(row => row.EndUtc!.Value);
        var nextBoundary = Min(nextCurrentEnd, earliestFuture);

        return new EventCenterRuntimeSnapshot
        {
            EvaluatedAtUtc = utcNow,
            Rows = runtimeRows,
            CurrentScheduleIds = current
                .OrderBy(row => row.StartUtc)
                .ThenBy(row => row.ScheduleId)
                .Select(row => row.ScheduleId)
                .ToArray(),
            UpcomingScheduleIds = nextUpcoming,
            DeferredScheduleIds = deferred
                .OrderBy(row => row.ScheduleId)
                .Select(row => row.ScheduleId)
                .ToArray(),
            EndedScheduleIds = ended
                .OrderBy(row => row.ScheduleId)
                .Select(row => row.ScheduleId)
                .ToArray(),
            NextBoundaryUtc = nextBoundary
        };
    }

    private static EventCenterRuntimeRow Deferred(
        EventCenterRowProjection projection,
        EventCenterRuntimeDeferReason reason) => new()
    {
        Projection = projection,
        State = EventCenterRuntimeRowState.Deferred,
        DeferReason = reason
    };

    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return left.Value <= right.Value ? left : right;
    }
}

/// <summary>Stateful wrapper that makes initialization, refresh, and restart replay observable.</summary>
public sealed class EventCenterRuntimeMachine
{
    private readonly object _gate = new();
    private EventCenterRowCatalog _catalog;
    private EventCenterRuntimeSnapshot _snapshot = EventCenterRuntimeSnapshot.Empty;

    /// <summary>The latest immutable snapshot.</summary>
    public EventCenterRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_gate) return _snapshot;
        }
    }

    /// <summary>Loads a catalog and reconstructs the state as a fresh boot/restart.</summary>
    public EventCenterRuntimeUpdate Initialize(EventCenterRowCatalog catalog, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_gate)
        {
            _catalog = catalog;
            return Apply(EventCenterRuntimeEvaluator.Evaluate(catalog.Rows, now), isReplay: true);
        }
    }

    /// <summary>Re-evaluates the loaded catalog at a later wall-clock instant.</summary>
    public EventCenterRuntimeUpdate Refresh(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_catalog == null)
            {
                throw new InvalidOperationException(
                    "Event-center runtime must be initialized before it can refresh.");
            }

            return Apply(EventCenterRuntimeEvaluator.Evaluate(_catalog.Rows, now), isReplay: false);
        }
    }

    private EventCenterRuntimeUpdate Apply(EventCenterRuntimeSnapshot next, bool isReplay)
    {
        var previous = _snapshot;
        var previousCurrent = previous.CurrentScheduleIds;
        var previousUpcoming = previous.UpcomingScheduleIds;
        var started = next.CurrentScheduleIds.Except(previousCurrent).OrderBy(id => id).ToArray();
        var ended = previousCurrent.Except(next.CurrentScheduleIds).OrderBy(id => id).ToArray();
        var currentChanged = !previousCurrent.SequenceEqual(next.CurrentScheduleIds);
        var upcomingChanged = !previousUpcoming.SequenceEqual(next.UpcomingScheduleIds);
        var boundaryChanged = previous.NextBoundaryUtc != next.NextBoundaryUtc;

        _snapshot = next;
        return new EventCenterRuntimeUpdate
        {
            Snapshot = next,
            StartedScheduleIds = started,
            EndedScheduleIds = ended,
            CurrentChanged = currentChanged,
            UpcomingChanged = upcomingChanged,
            BoundaryChanged = boundaryChanged,
            IsReplay = isReplay
        };
    }
}
