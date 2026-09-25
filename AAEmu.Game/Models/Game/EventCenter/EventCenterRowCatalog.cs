using System.Collections.ObjectModel;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Schedules;

namespace AAEmu.Game.Models.Game.EventCenter;

/// <summary>
/// The projected event-board view of every content schedule row, plus the gap tally that says how far it
/// is from being writable. Built on demand: the projection reads content, it does not schedule,
/// transition or announce anything.
/// </summary>
public sealed class EventCenterRowCatalog
{
    public static EventCenterRowCatalog Empty { get; } = new([]);

    private EventCenterRowCatalog(IReadOnlyList<EventCenterRowProjection> rows)
    {
        Rows = rows;
        GapCounts = new ReadOnlyDictionary<EventCenterRowGap, int>(
            rows.SelectMany(row => Bits(row.Gaps))
                .GroupBy(gap => gap)
                .ToDictionary(group => group.Key, group => group.Count()));
    }

    /// <summary>One projection per content row, ordered by schedule id.</summary>
    public IReadOnlyList<EventCenterRowProjection> Rows { get; }

    /// <summary>How many rows carry each gap.</summary>
    public IReadOnlyDictionary<EventCenterRowGap, int> GapCounts { get; }

    /// <summary>Rows whose period resolved to a positive-length instant pair.</summary>
    public int RowsWithPeriod => Rows.Count(row => row.IsPeriodResolved);

    /// <summary>Rows that could be written to a client as they stand. Zero until the missing sources exist.</summary>
    public int WireReadyRows => Rows.Count(row => row.IsWireReady);

    /// <summary>How many rows carry <paramref name="gap"/>.</summary>
    public int CountOf(EventCenterRowGap gap) => GapCounts.TryGetValue(gap, out var count) ? count : 0;

    /// <summary>
    /// Projects every row. Two rows with the same id are a content fault and fail loudly.
    /// <paramref name="boundSpawnerLookup"/> is diagnostics only and may be null.
    /// </summary>
    public static EventCenterRowCatalog Build(
        IEnumerable<GameSchedules> rows,
        Func<int, IReadOnlyList<uint>> boundSpawnerLookup = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var seen = new HashSet<int>();
        var projections = new List<EventCenterRowProjection>();
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (!seen.Add(row.Id))
            {
                throw new InvalidOperationException(
                    $"Two game schedule rows share id {row.Id}; the event row projection cannot key them.");
            }

            var boundSpawners = boundSpawnerLookup?.Invoke(row.Id) ?? [];
            projections.Add(EventCenterRowProjector.Project(row, boundSpawners));
        }

        projections.Sort((left, right) => left.ScheduleId.CompareTo(right.ScheduleId));
        return new EventCenterRowCatalog(projections);
    }

    /// <summary>Projects every row the schedule manager loaded, with the spawner links it loaded beside them.</summary>
    public static EventCenterRowCatalog Build(IGameScheduleManager scheduleManager)
    {
        ArgumentNullException.ThrowIfNull(scheduleManager);
        return Build(scheduleManager.GetSchedules().Values, scheduleManager.GetSpawnerIdsForSchedule);
    }

    private static IEnumerable<EventCenterRowGap> Bits(EventCenterRowGap gaps)
    {
        var remaining = gaps;
        while (remaining != EventCenterRowGap.None)
        {
            var lowest = remaining & ~(remaining - 1);
            yield return lowest;
            remaining &= ~lowest;
        }
    }
}
