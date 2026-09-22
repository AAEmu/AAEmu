namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The offense side of each zone group's raid team, held in memory so the siege_offense_hq_user relation can be
/// tested per unit without a database round trip. Built once per zone group by the caller's <c>load</c>, and
/// dropped when a registration changes.
/// </summary>
/// <remarks>
/// The relation runs once per unit per area-trigger pass, and the extended offense HQ's clout (doodad 10561)
/// ticks every 200 ms, so reading siege_raid_team_members per unit put two blocking MySQL queries per player per
/// tick on the tick thread. Nothing but the manager's own register/unregister and a character deletion writes
/// siege_raid_team_members, and those are the refresh points: <see cref="Invalidate"/> is called from
/// SiegeManager.RegisterForRaidTeam and SiegeManager.UnregisterFromRaidTeam, and from
/// CharacterManager.DeleteCharacterAssets for a character who is soft-deleted - the roster query drops a deleted
/// character (c.deleted = 0), so a cached set has to be dropped with it.
/// A cached set is copied at the boundary, so a caller cannot mutate what the next caller sees.
/// </remarks>
internal sealed class SiegeOffenseRosterCache
{
    private readonly Func<ushort, HashSet<uint>> _load;
    private readonly Dictionary<ushort, HashSet<uint>> _rosters = [];
    private readonly object _lock = new();

    /// <param name="load">Reads the offense roster of one zone group from the database.</param>
    internal SiegeOffenseRosterCache(Func<ushort, HashSet<uint>> load) => _load = load;

    /// <summary>The zone group's offense roster, read at most once per change to the registrations.</summary>
    internal IReadOnlySet<uint> Get(ushort zoneGroupId)
    {
        lock (_lock)
        {
            if (!_rosters.TryGetValue(zoneGroupId, out var roster))
                _rosters[zoneGroupId] = roster = _load(zoneGroupId);

            return new HashSet<uint>(roster);
        }
    }

    /// <summary>Drops the cached roster of one zone group, so the next read picks up the registration change.</summary>
    internal void Invalidate(ushort zoneGroupId)
    {
        lock (_lock)
            _rosters.Remove(zoneGroupId);
    }

    /// <summary>Drops every cached roster. For a change that is not tied to one zone group, such as a deletion.</summary>
    internal void InvalidateAll()
    {
        lock (_lock)
            _rosters.Clear();
    }
}
