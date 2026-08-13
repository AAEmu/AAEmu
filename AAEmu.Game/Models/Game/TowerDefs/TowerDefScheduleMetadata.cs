using System.Globalization;

namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// Applies Event Center schedule overlays onto loaded <c>tower_defs</c> rows.
/// </summary>
/// <remarks>
/// <c>tower_defs</c> has no schedule-mode column. Weekday <see cref="TowerDef.StartTimes"/> uniquely
/// identify WallClock events. Game-Time vs Manual cannot be derived from <c>tod</c> /
/// <c>tod_day_interval</c> / <c>target_npc_spawner_id</c> alone — those columns are populated on
/// both auto-armed Event Center rows and GM-only rows that share a portal spawner.
/// <see cref="AAEmu.Game.Models.Game.TowerDefsConfig.GameTimeAutoArmIds"/> is the Game-Time membership
/// overlay; <see cref="AAEmu.Game.Models.Game.TowerDefsConfig.WallClockStartTimesById"/> fills empty UTC
/// weekday slots only (never overwrites an existing StartTimes value).
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

    public readonly record struct WallClockApplyResult(
        int AppliedSlots,
        IReadOnlyList<uint> UnknownIds,
        IReadOnlyList<string> InvalidEntries,
        IReadOnlyList<string> Conflicts);

    public readonly record struct FollowOnApplyResult(
        int Applied,
        IReadOnlyList<uint> UnknownSourceIds,
        IReadOnlyList<uint> UnknownTargetIds,
        IReadOnlyList<uint> SelfRefs);

    /// <summary>
    /// True when the row has the Game-Time columns but no weekday slots — a completeness candidate.
    /// </summary>
    public static bool IsToDCapable(TowerDef towerDef) =>
        towerDef != null &&
        !towerDef.IsScheduled &&
        towerDef.TimeOfDayDayInterval > 0 &&
        towerDef.TargetNpcSpawnId != 0;

    /// <summary>
    /// Writes <see cref="TowerDef.FollowOnTowerDefId"/> from config (by id only).
    /// </summary>
    public static FollowOnApplyResult ApplyFollowOn(
        IEnumerable<TowerDef> towerDefs,
        IReadOnlyDictionary<uint, uint> followOnById)
    {
        var byId = new Dictionary<uint, TowerDef>();
        foreach (var towerDef in towerDefs)
        {
            if (towerDef == null || towerDef.Id == 0)
                continue;
            towerDef.FollowOnTowerDefId = 0;
            byId[towerDef.Id] = towerDef;
        }

        var unknownSource = new List<uint>();
        var unknownTarget = new List<uint>();
        var selfRefs = new List<uint>();
        var applied = 0;
        var overlay = followOnById ?? new Dictionary<uint, uint>();

        foreach (var (sourceId, targetId) in overlay)
        {
            if (sourceId == 0 || targetId == 0)
                continue;
            if (!byId.TryGetValue(sourceId, out var source))
            {
                unknownSource.Add(sourceId);
                continue;
            }

            if (!byId.ContainsKey(targetId))
            {
                unknownTarget.Add(targetId);
                continue;
            }

            if (sourceId == targetId)
            {
                selfRefs.Add(sourceId);
                continue;
            }

            source.FollowOnTowerDefId = targetId;
            applied++;
        }

        unknownSource.Sort();
        unknownTarget.Sort();
        selfRefs.Sort();
        return new FollowOnApplyResult(applied, unknownSource, unknownTarget, selfRefs);
    }

    /// <summary>
    /// Fills empty weekday StartTimes from config. Never overwrites a slot that already has a value;
    /// conflicting overlays are reported so a stale config cannot replace server data.
    /// </summary>
    public static WallClockApplyResult ApplyWallClockStartTimes(
        IEnumerable<TowerDef> towerDefs,
        IReadOnlyDictionary<uint, Dictionary<string, string>> wallClockStartTimesById)
    {
        var byId = new Dictionary<uint, TowerDef>();
        foreach (var towerDef in towerDefs)
        {
            if (towerDef == null || towerDef.Id == 0)
                continue;
            byId[towerDef.Id] = towerDef;
        }

        var unknown = new List<uint>();
        var invalid = new List<string>();
        var conflicts = new List<string>();
        var applied = 0;
        var overlay = wallClockStartTimesById ?? new Dictionary<uint, Dictionary<string, string>>();

        foreach (var (id, dayMap) in overlay)
        {
            if (id == 0)
                continue;
            if (!byId.TryGetValue(id, out var towerDef))
            {
                unknown.Add(id);
                continue;
            }

            if (dayMap == null || dayMap.Count == 0)
                continue;

            foreach (var (dayKey, timeText) in dayMap)
            {
                if (!TryParseDayOfWeek(dayKey, out var day))
                {
                    invalid.Add($"{id}:{dayKey}");
                    continue;
                }

                if (!TryParseDayTime(timeText, out var slot))
                {
                    invalid.Add($"{id}:{dayKey}={timeText}");
                    continue;
                }

                var dayIndex = (int)day;
                var existing = towerDef.StartTimes[dayIndex];
                if (existing.HasValue)
                {
                    if (existing.Value != slot)
                        conflicts.Add($"{id}:{day} existing={existing.Value:c} overlay={slot:c}");
                    continue;
                }

                towerDef.StartTimes[dayIndex] = slot;
                applied++;
            }
        }

        unknown.Sort();
        invalid.Sort(StringComparer.Ordinal);
        conflicts.Sort(StringComparer.Ordinal);
        return new WallClockApplyResult(applied, unknown, invalid, conflicts);
    }

    private static bool TryParseDayOfWeek(string key, out DayOfWeek day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (Enum.TryParse(key, ignoreCase: true, out day) &&
            Enum.IsDefined(typeof(DayOfWeek), day))
            return true;
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
            n is >= 0 and <= 6)
        {
            day = (DayOfWeek)n;
            return true;
        }

        return false;
    }

    private static bool TryParseDayTime(string text, out TimeSpan slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!TimeSpan.TryParseExact(
                text.Trim(),
                ["h\\:mm", "hh\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss"],
                CultureInfo.InvariantCulture,
                out slot))
            return false;
        if (slot < TimeSpan.Zero || slot >= TimeSpan.FromDays(1))
            return false;
        return true;
    }

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
