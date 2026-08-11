namespace AAEmu.Game.Models.Game.TowerDefs;

public class TowerDef
{
    /// <summary>
    /// Event Center "Game Time" rifts are identified by spawner-backed <c>tower_defs</c> rows whose
    /// names match these substrings (Korean client data / classic localization).
    /// </summary>
    private static readonly string[] GameTimeRiftNameMarkers =
    [
        "징조의 틈",   // Crimson Rift
        "전장의 안개",  // Grimghast
        "망각의 균열",  // Oblivion Rift
        "기계의 소란"   // Clockwork Rebellion
    ];

    /// <summary>Expand/hard-mode suffix in rift names (paired base + expand rows share a portal).</summary>
    private const string ExpandNameMarker = "확장";

    /// <summary>Guide/tutorial variant of Grimghast (separate spawner; not the night main event).</summary>
    private const string GrimghastGuideNameMarker = "안내";

    public uint Id { get; set; }
    public string Name { get; set; }
    public string StartMsg { get; set; }
    public string EndMsg { get; set; }
    public string TitleMsg { get; set; }
    public float TimeOfDay { get; set; }
    public float FirstWaveAfter { get; set; }
    public uint TargetNpcSpawnId { get; set; }
    public uint KillNpcId { get; set; }
    public uint KillNpcCount { get; set; }
    public float ForceEndTime { get; set; }
    public uint TimeOfDayDayInterval { get; set; }
    public uint MilestoneId { get; set; }
    public bool BroadcastToWholeWorld { get; set; }
    public uint StartDayOfWeekBit { get; set; }

    /// <summary>
    /// Wall-clock start time per day of the week, index 0 = Sunday, or null on days the event does
    /// not run. The <c>tower_defs</c> row carries seven independent <c>start_hourN</c> /
    /// <c>start_minuteN</c> pairs and the data uses them as one slot per weekday rather than seven
    /// starts in a single day — rows that vary them make this plain: 붉은 용의 출현 통합 인던 runs
    /// 21:30 on some days and 21:40 on others, 풍랑의 전조 runs 23:00 on two days and 22:00 on the
    /// rest, and 크라켄의 출현 populates exactly one slot.
    /// </summary>
    public TimeSpan?[] StartTimes { get; } = new TimeSpan?[7];

    /// <summary>Start time for a day, or null when this event does not run that day.</summary>
    public TimeSpan? StartTimeFor(DayOfWeek day) => StartTimes[(int)day];

    /// <summary>True when any weekday carries a wall-clock start time (UTC on World).</summary>
    public bool IsScheduled
    {
        get
        {
            foreach (var slot in StartTimes)
            {
                if (slot.HasValue)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Event-Center "Game Time" strip: no wall-clock hours, has a day interval, armed spawner,
    /// and a name marker for rift-family content. Driven by zone-simulated hour, not UTC.
    /// </summary>
    /// <remarks>
    /// Shipped rows pair base + expand on the same portal. Event Center Game Time strip:
    /// Grimghast @0 (base), Crimson @12 Cinderstone/Ynystere + @18 Auroria (expand only),
    /// Oblivion/Clockwork @2/@14. Base Crimson still has <c>tod=0</c> in data — arming every
    /// marker twin-spawns Crimson with Grimghast at night. Expand Grimghast / guide rows stay
    /// GM-startable, not auto Game-Time.
    /// </remarks>
    public bool IsGameTimeScheduled
    {
        get
        {
            if (IsScheduled)
                return false;
            if (TimeOfDayDayInterval == 0)
                return false;
            if (TargetNpcSpawnId == 0)
                return false;
            if (string.IsNullOrEmpty(Name))
                return false;
            if (Name.Contains("[테스트]", StringComparison.Ordinal) ||
                Name.Contains("[TEST]", StringComparison.OrdinalIgnoreCase))
                return false;

            var isExpand = Name.Contains(ExpandNameMarker, StringComparison.Ordinal);
            var isCrimson = Name.Contains(GameTimeRiftNameMarkers[0], StringComparison.Ordinal);
            var isGrimghast = Name.Contains(GameTimeRiftNameMarkers[1], StringComparison.Ordinal);

            // Crimson: only expand rows (tod 12 Cinderstone/Ynystere, 18 Auroria).
            if (isCrimson)
                return isExpand;

            // Grimghast main: base only. Skip expand (shares portal) and "안내" guides.
            if (isGrimghast)
            {
                if (isExpand)
                    return false;
                if (Name.Contains(GrimghastGuideNameMarker, StringComparison.Ordinal))
                    return false;
                return true;
            }

            foreach (var marker in GameTimeRiftNameMarkers)
            {
                if (Name.Contains(marker, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// How long the event stays up once started. <c>force_end_time</c> is the hard stop; rows that
    /// leave it at zero run until their progression steps finish, for which an hour is the longest
    /// window any shipped progression uses.
    /// </summary>
    public TimeSpan Duration => ForceEndTime > 0f
        ? TimeSpan.FromSeconds(ForceEndTime)
        : TimeSpan.FromHours(1);

    /// <summary>
    /// True when <paramref name="now"/> falls inside this event's wall-clock window. A window that
    /// runs past midnight stays owned by the day it started on, so yesterday's slot is checked as well.
    /// </summary>
    public bool IsWithinWindow(DateTime now)
    {
        return InSlot(now, now.DayOfWeek, 0) ||
               InSlot(now, now.AddDays(-1).DayOfWeek, -1);
    }

    /// <summary>
    /// True when simulated game hour advances across this row's <c>tod</c> (handles day wrap).
    /// </summary>
    public bool CrossedGameStartHour(float oldHour, float newHour)
    {
        var trigger = TimeOfDay;
        if (trigger < 0f)
            trigger = 0f;
        if (trigger >= 24f)
            trigger = 0f;

        oldHour = NormalizeHour(oldHour);
        newHour = NormalizeHour(newHour);
        if (Math.Abs(oldHour - newHour) < 1e-6f)
            return false;

        if (oldHour <= newHour)
            return oldHour < trigger && trigger <= newHour;

        // Wrapped past 24 → 0.
        return oldHour < trigger || trigger <= newHour;
    }

    private static float NormalizeHour(float hours)
    {
        var h = hours % 24f;
        return h < 0f ? h + 24f : h;
    }

    private bool InSlot(DateTime now, DayOfWeek day, int dayOffset)
    {
        if (StartTimeFor(day) is not { } slot)
            return false;

        var start = now.Date.AddDays(dayOffset).Add(slot);
        return now >= start && now < start + Duration;
    }

    public List<TowerDefProg> Progs;
}
