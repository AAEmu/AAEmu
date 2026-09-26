using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.PlotAuctions;

namespace AAEmu.Game.Models.Game.SailingActivity;

/// <summary>
/// Resolves an activity's authored window into the two 64-bit stamps
/// <c>SC 0x391</c> carries.
/// </summary>
/// <remarks>
/// <para>
/// <c>game_activities</c> stores <c>start_time</c>/<c>end_time</c> as text plus a <c>time_mode</c>,
/// and the shipped rows use two different shapes. Two activities write the self-describing form
/// <c>year|month|day|hour|minute</c>. The other two write a bare integer — <c>0</c> and
/// <c>50</c> for one, <c>0</c> and <c>14</c> for the other.
/// </para>
/// <para>
/// A bare integer states no unit and no reference point. Whether it counts hours, days or stages,
/// and whether it runs from server start, from the client's local clock or from the moment the
/// player first opens the panel, is not recoverable from this table. So the bare form is refused
/// rather than guessed: <see cref="TryResolve"/> returns false with a reason, and
/// <c>SC 0x391</c> never carries a stamp this class invented.
/// </para>
/// <para>
/// The five-part form needs no assumption about what <c>time_mode</c> is called, because the value
/// describes itself. That is why it is accepted and the integer form is not.
/// </para>
/// </remarks>
public static class SailingActivityWindow
{
    /// <summary>
    /// The stamp used when a window could not be resolved. Zero, never a fabricated instant:
    /// a caller that would send it is expected to drop the row instead.
    /// </summary>
    public const ulong UnresolvedStamp = 0UL;

    /// <summary>
    /// True when <paramref name="raw"/> is the self-describing <c>year|month|day|hour|minute</c> form.
    /// </summary>
    public static bool IsAbsoluteForm(string raw) => (raw ?? string.Empty).Split('|').Length == 5;

    /// <summary>
    /// Resolves both stamps, or refuses both.
    /// </summary>
    /// <param name="startRaw">The <c>start_time</c> cell, verbatim.</param>
    /// <param name="endRaw">The <c>end_time</c> cell, verbatim.</param>
    /// <param name="startUtc">Resolved start, or <see cref="DateTime.MinValue"/> on refusal.</param>
    /// <param name="endUtc">Resolved end, or <see cref="DateTime.MinValue"/> on refusal.</param>
    /// <param name="reason">Why the window was refused, or an empty string on success.</param>
    public static bool TryResolve(string startRaw, string endRaw, out DateTime startUtc, out DateTime endUtc, out string reason)
    {
        startUtc = endUtc = default;
        reason = string.Empty;

        if (!IsAbsoluteForm(startRaw) || !IsAbsoluteForm(endRaw))
        {
            reason =
                $"start_time '{startRaw}' / end_time '{endRaw}' is not the year|month|day|hour|minute form; " +
                "the bare-integer form carries no unit or reference point, so the window is left unresolved";
            return false;
        }

        if (!PlotAuctionRules.TryParseSchedule(startRaw, out startUtc))
        {
            reason = $"start_time '{startRaw}' is not a valid year|month|day|hour|minute stamp";
            startUtc = default;
            return false;
        }

        if (!PlotAuctionRules.TryParseSchedule(endRaw, out endUtc))
        {
            reason = $"end_time '{endRaw}' is not a valid year|month|day|hour|minute stamp";
            startUtc = endUtc = default;
            return false;
        }

        if (endUtc <= startUtc)
        {
            reason = $"end_time '{endRaw}' is not after start_time '{startRaw}'";
            startUtc = endUtc = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The stamp as <c>SC 0x391</c> writes it, or <see cref="UnresolvedStamp"/> when unresolved.
    /// </summary>
    public static ulong ToWireStamp(DateTime utc) => (ulong)ServerCalendar.AsUtc(utc).Ticks;
}
