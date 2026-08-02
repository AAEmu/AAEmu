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
        if (NextPhase <= 0)
            return false;

        var currentTime = IsRealtime
            ? (float)DateTime.Now.TimeOfDay.TotalHours
            : TimeManager.Instance.GetTime;

        // Ranged descriptors are entry conditions. Point triggers normally wait for the next
        // crossing; templates marked force_tod_top_priority (for example lamps) resolve their
        // current phase immediately when initialized.
        var shouldChange = TodEnd >= 0
            ? IsWithinWindow(currentTime)
            : owner.Template.ForceTodTopPriority && currentTime >= TodAsHours;
        if (!shouldChange)
            return false;

        Logger.Trace(
            "DoodadFuncTod: currentTime {0}, Tod {1}, TodEnd {2}, IsRealtime {3}, OverridePhase {4}",
            currentTime, Tod, TodEnd, IsRealtime, NextPhase);
        owner.OverridePhase = NextPhase;
        return true;
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
