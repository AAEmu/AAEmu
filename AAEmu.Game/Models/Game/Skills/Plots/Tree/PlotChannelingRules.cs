namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Cast-bar vs channel wait on a plot node, and which queued event a bite/cancel must resume.
/// </summary>
public static class PlotChannelingRules
{
    public static (int CastingMs, int ChannelingMs) NextEdgeDurations(
        IEnumerable<(bool Casting, bool Channeling, int DelayMs)> edges)
    {
        var castingMs = 0;
        var channelingMs = 0;
        foreach (var (casting, channeling, delayMs) in edges)
        {
            if (casting)
                castingMs = Math.Max(castingMs, delayMs);
            if (channeling)
                channelingMs = Math.Max(channelingMs, delayMs);
        }

        return (castingMs, channelingMs);
    }

    /// <summary>
    /// SC plot times are milliseconds / 10, same as skill cast-time wire.
    /// </summary>
    public static ushort ToPlotWireTime(int delayMs) =>
        (ushort)Math.Clamp(delayMs / 10, 0, ushort.MaxValue);

    /// <summary>
    /// The client holds the plot cast/channel for the packet time only. It does not
    /// add <c>add_anim_cs_time</c> itself, so the wire must include that wait or the
    /// throw pose drops when the delay elapses and the next event is still queued.
    /// </summary>
    public static int IncludeAnimCsTime(int delayMs, bool addAnimCsTime, int animCsTimeMs)
    {
        if (!addAnimCsTime || animCsTimeMs <= 0)
            return delayMs;
        if (delayMs <= 0)
            return animCsTimeMs;
        return delayMs + animCsTimeMs;
    }

    public const int IgnoredStopRefreshMinMs = 400;

    /// <summary>
    /// Re-send the last plot event after an ignored CSStopCasting, but not on every
    /// retry the client fires while the local pose is already idle.
    /// </summary>
    public static bool ShouldRefreshPlotAfterIgnoredStop(
        bool hasLastEvent,
        DateTime lastRefreshUtc,
        DateTime nowUtc)
    {
        if (!hasLastEvent)
            return false;
        if (lastRefreshUtc != default &&
            (nowUtc - lastRefreshUtc).TotalMilliseconds < IgnoredStopRefreshMinMs)
            return false;
        return true;
    }

    /// <summary>
    /// The channel wait is the child entered by a channeling edge. A bite-roll loop
    /// queued from the same parent is not that wait — resuming it would swallow the hook.
    /// </summary>
    public static int IndexOfChannelWait<T>(IReadOnlyList<T> items, Func<T, bool> enteredByChannelingEdge)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (enteredByChannelingEdge(items[i]))
                return i;
        }

        return -1;
    }
}
