using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Models.Game.Hero;

/// <summary>
/// enum_hero_schedule_events, and the order a season runs its phases in.
/// </summary>
/// <remarks>
/// The client names these itself, in x2ui/hero/common.lua:1-4, and passes them straight back to
/// X2Hero:GetActivedHeroPeriod - so these values are the wire contract, not an internal convention.
/// </remarks>
public enum HeroPhase : byte
{
    /// <summary>No phase is running. Not a schedule value; the gap between seasons.</summary>
    None = 0,

    /// <summary>Leadership accrues and the ladder ranks. The long one.</summary>
    LeadershipRanking = 1,

    /// <summary>The ladder is frozen into a candidate list and candidates may decline to stand.</summary>
    HeroAbstain = 2,

    /// <summary>The ballot is open.</summary>
    HeroVoting = 3,

    /// <summary>The elected heroes serve.</summary>
    HeroPeriod = 4
}

/// <summary>One hero_schedules row: when a phase of a season runs.</summary>
public readonly record struct HeroScheduleWindow(uint Season, HeroPhase Phase, DateTime Start, DateTime End)
{
    public bool Contains(DateTime moment) => Start <= moment && moment < End;
}

/// <summary>
/// The hero_schedules table, cached.
/// </summary>
/// <remarks>
/// Static content, read once. Kept separate from HeroSeason - which answers only "which season" - because
/// the election needs the individual windows, not the season's outer bounds.
/// </remarks>
public static class HeroSchedule
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly Lock Gate = new();
    private static List<HeroScheduleWindow> _windows;

    /// <summary>Every window of every season, in start order.</summary>
    public static IReadOnlyList<HeroScheduleWindow> All
    {
        get
        {
            EnsureLoaded();
            return _windows;
        }
    }

    /// <summary>The windows of one season, in phase order.</summary>
    public static IReadOnlyList<HeroScheduleWindow> ForSeason(uint season) =>
        [.. All.Where(w => w.Season == season).OrderBy(w => w.Phase)];

    /// <summary>The window of one phase of one season, if the data has it.</summary>
    public static HeroScheduleWindow? Find(uint season, HeroPhase phase)
    {
        foreach (var window in All)
        {
            if (window.Season == season && window.Phase == phase)
                return window;
        }

        return null;
    }

    /// <summary>
    /// The phase a season is in at a given moment, or None between phases.
    /// </summary>
    /// <remarks>
    /// The shipped windows do not tile: season 5 runs leadership_ranking to 2026-09-15 and picks
    /// hero_abstain up on the 20th. A gap is genuinely "no phase", and saying so is what lets the
    /// override in HeroElectionManager be distinguishable from the schedule agreeing with it.
    /// </remarks>
    public static HeroPhase PhaseAt(uint season, DateTime moment)
    {
        foreach (var window in All)
        {
            if (window.Season == season && window.Contains(moment))
                return window.Phase;
        }

        return HeroPhase.None;
    }

    private static void EnsureLoaded()
    {
        if (_windows != null)
            return;

        lock (Gate)
        {
            if (_windows != null)
                return;

            var windows = new List<HeroScheduleWindow>();
            try
            {
                using var connection = SQLite.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT hero_id, event_id, start, end FROM hero_schedules ORDER BY start";
                command.Prepare();

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var phase = Convert.ToByte(reader["event_id"]);
                    if (phase is < (byte)HeroPhase.LeadershipRanking or > (byte)HeroPhase.HeroPeriod)
                    {
                        Logger.Warn("HeroSchedule: ignoring row with unknown event_id {0}", phase);
                        continue;
                    }

                    windows.Add(new HeroScheduleWindow(
                        Convert.ToUInt32(reader["hero_id"]),
                        (HeroPhase)phase,
                        Convert.ToDateTime(reader["start"]),
                        Convert.ToDateTime(reader["end"])));
                }

                Logger.Info("Loaded {0} hero schedule windows across {1} seasons",
                    windows.Count, windows.Select(w => w.Season).Distinct().Count());
            }
            catch (Exception ex)
            {
                // Cached empty: a missing game database will not fix itself on the next read, and every
                // caller already treats "no window" as None.
                Logger.Error(ex, "HeroSchedule: failed to read hero_schedules");
            }

            _windows = windows;
        }
    }
}
