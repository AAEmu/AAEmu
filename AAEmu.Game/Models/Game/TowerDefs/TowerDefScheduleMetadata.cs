namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// Applies Event Center Game-Time membership onto loaded <c>tower_defs</c> rows.
/// </summary>
/// <remarks>
/// <c>tower_defs</c> has no schedule-mode column. Weekday <see cref="TowerDef.StartTimes"/> uniquely
/// identify WallClock events. Game-Time vs Manual cannot be derived from <c>tod</c> /
/// <c>tod_day_interval</c> / <c>target_npc_spawner_id</c> alone — those columns are populated on
/// both auto-armed Event Center rows and GM-only rows that share a portal spawner.
/// <see cref="AAEmu.Game.Models.Game.TowerDefsConfig.GameTimeAutoArmIds"/> is therefore the explicit membership overlay.
/// Omitted ToD-capable rows stay Manual and are reported so the overlay cannot go stale silently.
/// Family/variant labels are not required: portal exclusion uses
/// <see cref="TowerDef.TargetNpcSpawnId"/>.
/// </remarks>
public static class TowerDefScheduleMetadata
{
    public readonly record struct ApplyResult(
        int AppliedGameTime,
        IReadOnlyList<uint> UnknownIds,
        IReadOnlyList<uint> WallClockConflicts,
        IReadOnlyList<uint> UnlistedToDCandidates,
        IReadOnlyList<uint> IneligibleIds);

    /// <summary>
    /// True when the row has the Game-Time columns but no weekday slots — a completeness candidate.
    /// </summary>
    public static bool IsToDCapable(TowerDef towerDef) =>
        towerDef != null &&
        !towerDef.IsScheduled &&
        towerDef.TimeOfDayDayInterval > 0 &&
        towerDef.TargetNpcSpawnId != 0;

    public static ApplyResult Apply(IEnumerable<TowerDef> towerDefs, IReadOnlyList<uint> gameTimeAutoArmIds)
    {
        var byId = new Dictionary<uint, TowerDef>();
        foreach (var towerDef in towerDefs)
        {
            if (towerDef == null || towerDef.Id == 0)
                continue;
            byId[towerDef.Id] = towerDef;
            towerDef.ScheduleMode = towerDef.IsScheduled
                ? TowerDefScheduleMode.WallClock
                : TowerDefScheduleMode.Manual;
        }

        var unknown = new List<uint>();
        var wallConflicts = new List<uint>();
        var ineligible = new List<uint>();
        var applied = 0;
        var listed = new HashSet<uint>();
        var ids = gameTimeAutoArmIds ?? Array.Empty<uint>();

        foreach (var id in ids)
        {
            if (id == 0 || !listed.Add(id))
                continue;
            if (!byId.TryGetValue(id, out var towerDef))
            {
                unknown.Add(id);
                continue;
            }

            if (towerDef.IsScheduled)
            {
                wallConflicts.Add(id);
                continue;
            }

            if (towerDef.TimeOfDayDayInterval == 0 || towerDef.TargetNpcSpawnId == 0)
            {
                ineligible.Add(id);
                continue;
            }

            towerDef.ScheduleMode = TowerDefScheduleMode.GameTime;
            applied++;
        }

        var unlisted = new List<uint>();
        foreach (var towerDef in byId.Values)
        {
            if (listed.Contains(towerDef.Id))
                continue;
            if (!IsToDCapable(towerDef))
                continue;
            unlisted.Add(towerDef.Id);
        }

        unlisted.Sort();
        unknown.Sort();
        wallConflicts.Sort();
        ineligible.Sort();
        return new ApplyResult(applied, unknown, wallConflicts, unlisted, ineligible);
    }
}
