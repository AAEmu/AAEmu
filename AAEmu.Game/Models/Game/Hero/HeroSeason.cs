using NLog;

namespace AAEmu.Game.Models.Game.Hero;

/// <summary>
/// Resolves which hero season the server should tell the client it is in.
/// </summary>
/// <remarks>
/// A placeholder for the season half of a HeroManager, which does not exist yet. It reads the schedule
/// and answers one question - what is the current season id - because several client behaviours are
/// keyed on having a season at all rather than on the election actually running.
///
/// The Hero window greys every ranking row when X2Hero:GetHeroCandidateCount() returns 0
/// (hero_rank.lua:32 picks brown_3 or gray_30 on rank &lt;= candidateCount). That count comes from
/// hero_conditions.hero_candidate_scope, which the client can only reach through a season: heros.id -&gt;
/// hero_condition_id -&gt; the scope. Send season 0 and it has nothing to resolve, so the count is 0 and
/// even rank 1 renders grey.
/// </remarks>
public static class HeroSeason
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly Lock Gate = new();
    private static List<SeasonBounds> _bounds;
    private static uint _lastReported = uint.MaxValue;

    /// <summary>The outer bounds of one season: its first window's start to its last window's end.</summary>
    private readonly record struct SeasonBounds(uint Season, DateTime Start, DateTime End);

    /// <summary>
    /// The hero season to advertise, or 0 when the schedule holds nothing usable.
    /// </summary>
    /// <remarks>
    /// Evaluated against the clock every time rather than cached, because a season boundary is a moment
    /// the server has to cross while it is running. It used to be resolved once and kept for the lifetime
    /// of the process, which meant the shipped 2026-08-15 rollover into season 5 would never happen on a
    /// server that had been up since before it - every phase stayed None until someone restarted.
    ///
    /// Nothing is re-read from the database to do that. The schedule itself is static content and
    /// HeroSchedule already holds it; only the comparison against "now" has to be repeated, and the
    /// per-season bounds it compares are folded once.
    /// </remarks>
    public static uint CurrentId => At(DateTime.UtcNow);

    /// <summary>The season that is running at a given moment.</summary>
    /// <remarks>
    /// Shipped schedules leave gaps between seasons - the 10.0.2.13 data runs season 4 to 2026-08-04 and
    /// picks season 5 up on 2026-08-15 - and during one the honest answer for the election is "none", but
    /// answering 0 would grey the whole ranking. So a gap falls back to the most recent season that has
    /// already started, which keeps the last known rules on screen.
    /// </remarks>
    public static uint At(DateTime moment)
    {
        uint current = 0, latestStarted = 0;

        foreach (var bounds in Bounds)
        {
            if (bounds.Start <= moment && moment < bounds.End)
                current = bounds.Season;
            if (bounds.Start <= moment)
                latestStarted = bounds.Season;
        }

        var result = current != 0 ? current : latestStarted;
        Report(result, current == 0);
        return result;
    }

    /// <summary>
    /// Logs the resolved season, but only when the answer is not the one already reported.
    /// </summary>
    /// <remarks>
    /// CurrentId is consulted on every ranking request and every leadership send, so logging each
    /// resolution would bury the log. Logging the changes makes the boundary crossing visible, which is
    /// the only interesting thing this class does.
    /// </remarks>
    private static void Report(uint season, bool betweenSeasons)
    {
        if (season == _lastReported)
            return;

        _lastReported = season;

        if (season == 0)
            Logger.Warn("HeroSeason: no season has started yet; the ranking will render greyed");
        else
            Logger.Info("HeroSeason: using season {0}{1}", season, betweenSeasons ? " (between seasons)" : "");
    }

    /// <summary>One entry per season, in start order, folded from the individual phase windows.</summary>
    private static IReadOnlyList<SeasonBounds> Bounds
    {
        get
        {
            if (_bounds != null)
                return _bounds;

            lock (Gate)
            {
                if (_bounds != null)
                    return _bounds;

                var byId = new Dictionary<uint, SeasonBounds>();
                foreach (var window in HeroSchedule.All)
                {
                    if (byId.TryGetValue(window.Season, out var bounds))
                    {
                        byId[window.Season] = bounds with
                        {
                            Start = window.Start < bounds.Start ? window.Start : bounds.Start,
                            End = window.End > bounds.End ? window.End : bounds.End
                        };
                    }
                    else
                    {
                        byId[window.Season] = new SeasonBounds(window.Season, window.Start, window.End);
                    }
                }

                _bounds = [.. byId.Values.OrderBy(b => b.Start)];
                return _bounds;
            }
        }
    }
}
