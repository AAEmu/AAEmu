using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncTod : DoodadPhaseFuncTemplate
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;

    /// <summary>Start time encoded as HHMM.</summary>
    public int Tod { get; set; }
    public int NextPhase { get; set; }

    /// <summary>Optional inclusive end time encoded as HHMM, or -1 for a point-in-time trigger.</summary>
    public int TodEnd { get; set; } = -1;

    /// <summary>Whether this trigger follows the server's wall clock instead of Zone game time.</summary>
    public bool IsRealtime { get; set; }

    /// <summary>Normalized start time in hours.</summary>
    public float TodAsHours { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        // Init already settled this graph against current clock; do not re-fire ToD jumps.
        // Runtime ToD edges are edge-crossing driven by TimeManager (and scheduled tasks).
        if (owner.SuppressTodPhaseOverride)
            return false;

        if (NextPhase <= 0)
            return false;

        if (!ShouldJumpAt(GetClockHours(), owner.Template.ForceTodTopPriority))
            return false;

        Logger.Trace(
            "DoodadFuncTod: currentTime {0}, Tod {1}, TodEnd {2}, IsRealtime {3}, OverridePhase {4}",
            GetClockHours(), Tod, TodEnd, IsRealtime, NextPhase);
        owner.OverridePhase = NextPhase;
        return true;
    }

    /// <summary>
    /// Pure evaluation for settle/init — whether this descriptor would leave its phase at
    /// <paramref name="hours"/> without side effects.
    /// </summary>
    public bool ShouldJumpAt(float hours, bool forceTodTopPriority)
    {
        if (NextPhase <= 0)
            return false;

        // Ranged descriptors are entry conditions for the next phase while the window is open.
        if (TodEnd >= 0)
            return IsWithinWindow(hours);

        // Point triggers normally wait for the next schedule-crossing on TimeManager.
        // force_tod_top_priority (lamps etc.) re-reads "period of day" on init only — settle walk.
        return forceTodTopPriority && NormalizeHours(hours) >= TodAsHours;
    }

    public float GetClockHours()
    {
        return IsRealtime
            ? (float)DateTime.Now.TimeOfDay.TotalHours
            : TimeManager.Instance.GetTime;
    }

    public bool IsWithinWindow(float hours)
    {
        if (TodEnd < 0)
            return false;

        var currentMinute = (int)Math.Floor(NormalizeHours(hours) * MinutesPerHour);
        var startMinute = ToMinuteOfDay(Tod);
        var endMinute = ToMinuteOfDay(TodEnd);
        return startMinute <= endMinute
            ? currentMinute >= startMinute && currentMinute <= endMinute
            : currentMinute >= startMinute || currentMinute <= endMinute;
    }

    public static float ToHours(int encodedTime)
    {
        var normalized = NormalizeEncodedTime(encodedTime);
        var hours = normalized / 100;
        var minutes = normalized % 100;
        return hours + minutes / (float)MinutesPerHour;
    }

    private static int ToMinuteOfDay(int encodedTime)
    {
        var normalized = NormalizeEncodedTime(encodedTime);
        return normalized / 100 * MinutesPerHour + normalized % 100;
    }

    private static int NormalizeEncodedTime(int encodedTime)
    {
        while (encodedTime >= HoursPerDay * 100)
            encodedTime /= 10;
        return encodedTime;
    }

    private static float NormalizeHours(float hours)
    {
        var normalized = hours % HoursPerDay;
        return normalized < 0 ? normalized + HoursPerDay : normalized;
    }
}
