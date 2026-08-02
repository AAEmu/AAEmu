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

    /// <summary>True when any weekday carries a start time.</summary>
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
    /// How long the event stays up once started. <c>force_end_time</c> is the hard stop; rows that
    /// leave it at zero run until their progression steps finish, for which an hour is the longest
    /// window any shipped progression uses.
    /// </summary>
    public TimeSpan Duration => ForceEndTime > 0f
        ? TimeSpan.FromSeconds(ForceEndTime)
        : TimeSpan.FromHours(1);

    /// <summary>
    /// True when <paramref name="now"/> falls inside this event's window. A window that runs past
    /// midnight stays owned by the day it started on, so yesterday's slot is checked as well.
    /// </summary>
    public bool IsWithinWindow(DateTime now)
    {
        return InSlot(now, now.DayOfWeek, 0) ||
               InSlot(now, now.AddDays(-1).DayOfWeek, -1);
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
