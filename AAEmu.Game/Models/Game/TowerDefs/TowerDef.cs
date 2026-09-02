namespace AAEmu.Game.Models.Game.TowerDefs;

public class TowerDef
{
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
    /// Optional follow-on <c>tower_defs.id</c> from <c>TowerDefs.FollowOnTowerDefById</c>.
    /// World starts it when this tower opens its final progression step (0 = none).
    /// </summary>
    public uint FollowOnTowerDefId { get; set; }

    /// <summary>Event family from typed schedule config (not display name).</summary>
    public TowerDefEventFamily Family { get; set; } = TowerDefEventFamily.Unspecified;

    /// <summary>Base / expand / guide from typed schedule config.</summary>
    public TowerDefEventVariant Variant { get; set; } = TowerDefEventVariant.Unspecified;

    /// <summary>
    /// How World auto-arms this row. WallClock comes from weekday StartTimes; GameTime comes from
    /// explicit <c>TowerDefs.GameTimeAutoArmIds</c> membership.
    /// </summary>
    public TowerDefScheduleMode ScheduleMode { get; set; } = TowerDefScheduleMode.Manual;

    /// <summary>
    /// Wall-clock start time per day of the week, index 0 = Sunday, or null on days the event does
    /// not run. The <c>tower_defs</c> row carries seven independent <c>start_hourN</c> /
    /// <c>start_minuteN</c> pairs used as one slot per weekday. A 00:00 pair means the event does
    /// not run that day. When <see cref="StartDayOfWeekBit"/> is non-zero, only days whose bit is
    /// set keep their hour (bit 0 = Sunday).
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
    /// Event-Center Game Time strip: ToD-driven auto-arm from typed schedule config.
    /// Requires day interval + seed spawner; never uses display-name substrings.
    /// </summary>
    public bool IsGameTimeScheduled
    {
        get
        {
            if (ScheduleMode != TowerDefScheduleMode.GameTime)
                return false;
            if (IsScheduled)
                return false;
            if (TimeOfDayDayInterval == 0)
                return false;
            if (TargetNpcSpawnId == 0)
                return false;
            return true;
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

    /// <summary>
    /// True when this event shares a portal spawner with <paramref name="other"/>.
    /// Portal exclusion uses this loaded relationship, not family labels.
    /// </summary>
    public bool SharesPortalSpawnerWith(TowerDef other) =>
        other != null &&
        TargetNpcSpawnId != 0 &&
        TargetNpcSpawnId == other.TargetNpcSpawnId;

    public List<TowerDefProg> Progs;
}
