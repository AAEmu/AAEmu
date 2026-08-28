using System.Collections.Concurrent;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Per-zone record of which buffs each dedicate has been told about, keyed by owning unit and buff
/// instance index, with the stack last accepted on Create. The zone's own bookkeeping
/// (<c>ZoneBuffMan</c>) only learns an entry from WZBuffCreated — its Change handler warns
/// <c>invalid buff id</c> and returns for an unregistered index, and its Destroy handler does the
/// same, so Updates and Removes must only be sent to zones that accepted the Create.
/// </summary>
public static class ZoneBuffRegistry
{
    private static readonly ConcurrentDictionary<(uint ZoneId, uint InstanceId), Dictionary<(uint Owner, uint Index), uint>> Accepted = new();

    /// <summary>
    /// Records that a Create for (unit, index) reached this zone instance at <paramref name="stack"/>.
    /// </summary>
    public static void MarkCreated(uint zoneId, uint instanceId, uint ownerObjId, uint buffIndex, uint stack = 1)
    {
        if (zoneId == 0)
            return;

        var set = Accepted.GetOrAdd((zoneId, instanceId), _ => new Dictionary<(uint, uint), uint>());
        lock (set)
            set[(ownerObjId, buffIndex)] = stack == 0 ? 1u : stack;
    }

    /// <summary>
    /// Whether this zone instance previously accepted the Create for (unit, index).
    /// </summary>
    public static bool WasCreated(uint zoneId, uint instanceId, uint ownerObjId, uint buffIndex) =>
        TryGetRecordedStack(zoneId, instanceId, ownerObjId, buffIndex, out _);

    /// <summary>
    /// Stack last written on Create for (unit, index), if any.
    /// </summary>
    public static bool TryGetRecordedStack(uint zoneId, uint instanceId, uint ownerObjId, uint buffIndex, out uint stack)
    {
        stack = 0;
        if (zoneId == 0)
            return false;

        if (!Accepted.TryGetValue((zoneId, instanceId), out var set))
            return false;

        lock (set)
            return set.TryGetValue((ownerObjId, buffIndex), out stack);
    }

    /// <summary>
    /// Drops one entry after a successful Remove relay.
    /// </summary>
    public static void Clear(uint zoneId, uint instanceId, uint ownerObjId, uint buffIndex)
    {
        if (!Accepted.TryGetValue((zoneId, instanceId), out var set))
            return;

        lock (set)
            set.Remove((ownerObjId, buffIndex));
    }

    /// <summary>
    /// Forgets every buff recorded against one unit in one zone instance.
    /// </summary>
    /// <remarks>
    /// Must be called whenever a unit is created in or removed from a zone. Object ids are recycled, so
    /// without this a new unit inherits the buff bookkeeping of whatever previously held its id: the
    /// zone is judged to already know buffs it has never been told about, their Creates are suppressed
    /// as duplicates, and the unit runs with none of them. A hull that inherited an id this way reached
    /// its zone with no speed buffs at all and would not move.
    /// </remarks>
    public static void ClearUnit(uint zoneId, uint instanceId, uint ownerObjId)
    {
        if (!Accepted.TryGetValue((zoneId, instanceId), out var set))
            return;

        lock (set)
        {
            var drop = new List<(uint Owner, uint Index)>();
            foreach (var key in set.Keys)
            {
                if (key.Owner == ownerObjId)
                    drop.Add(key);
            }

            foreach (var key in drop)
                set.Remove(key);
        }
    }

    /// <summary>
    /// Forgets one unit's buffs in <em>every</em> zone instance, for callers that do not know which zone
    /// holds it. Used when a unit is created, since a recycled id may carry entries from any zone the
    /// previous holder visited.
    /// </summary>
    public static void ClearUnitEverywhere(uint ownerObjId)
    {
        foreach (var set in Accepted.Values)
        {
            lock (set)
            {
                var drop = new List<(uint Owner, uint Index)>();
                foreach (var key in set.Keys)
                {
                    if (key.Owner == ownerObjId)
                        drop.Add(key);
                }

                foreach (var key in drop)
                    set.Remove(key);
            }
        }
    }

    /// <summary>
    /// Forgets everything a zone instance was told: its process lost the state with the TCP link,
    /// so nothing but fresh Creates can be valid until they are re-sent.
    /// </summary>
    public static void ResetZone(uint zoneId, uint instanceId) =>
        Accepted.TryRemove((zoneId, instanceId), out _);
}
