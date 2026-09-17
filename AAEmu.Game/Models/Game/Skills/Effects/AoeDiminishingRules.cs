using System.Collections.Concurrent;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// AoE diminishing: each successive hit of the same area skill on the same unit inside a window takes the
/// next rate from <c>aoe_diminishings</c>, and a reset effect clears the counter.
/// </summary>
/// <remarks>
/// The table is ten rows — 100, 95, 90, 85, 80, 75, 70, 65, 60, 50 — read as the rate for the counter's
/// position: the first hit is 100 %, the second 95 %, the third 90 %. Content beyond the tenth hit repeats
/// the last rate (50) rather than wrapping back to 100: the table is a decay, and wrapping would make the
/// eleventh hit cheaper than the tenth.
/// <para>
/// The counter is keyed on the skill as well as the unit, so two different area skills do not share a
/// decay — <c>aoe_diminishings</c> describes one skill's repeated application, not a global penalty on
/// being hit.
/// </para>
/// </remarks>
public static class AoeDiminishingRules
{
    /// <summary>
    /// How long a hit keeps the counter alive. Measured between hits, not since the first one.
    /// </summary>
    /// <remarks>
    /// Nothing in the content states the window; it is not a column anywhere. Ten seconds is long enough
    /// for the channeled and repeated area skills that carry <c>aoe_diminishing</c> on their plot events
    /// (불의 비 11939, 심판의 창 13286, 대지 가르기 10644 are 1-3 s apart per tick) and short enough that a
    /// fight's next cast starts clean.
    /// </remarks>
    public const double WindowSeconds = 10.0;

    /// <summary>Whether a skill's geometry makes it an area skill.</summary>
    /// <remarks>
    /// The same two columns <c>Skill</c> gathers its area targets with: <c>target_area_radius</c> and
    /// <c>target_area_count</c>. 8,501 skills set one or the other; a single-target skill (radius 0, count 1)
    /// is not an area skill and never diminishes.
    /// </remarks>
    public static bool IsAreaSkill(int targetAreaCount, int targetAreaRadius) =>
        targetAreaRadius > 0 || targetAreaCount > 1;

    /// <summary>
    /// Whether a hit diminishes at all: an area skill whose plot event is flagged, or a plot cast that is
    /// itself the flagged event.
    /// </summary>
    /// <remarks>
    /// <c>plot_events.aoe_diminishing</c> is set on 733 of the 51,178 rows and is the content's own marker —
    /// <c>PlotManager</c> loads it and nothing read it. A skill that names a flagged plot counts, and so does
    /// a plot cast that arrives with the flag on its own event, which is how 불의 비 and friends run their
    /// damage.
    /// </remarks>
    public static bool Diminishes(bool isAreaSkill, bool plotFlagged, bool plotCastFlagged) =>
        plotCastFlagged || (isAreaSkill && plotFlagged);

    /// <summary>
    /// The rate multiplier for the next hit.
    /// </summary>
    /// <remarks>
    /// <paramref name="previousRate"/> is the rate the last hit used and <paramref name="lastHit"/> when it
    /// landed. A hit more than <see cref="WindowSeconds"/> after the last one, or the first hit of a skill,
    /// takes the first row — which is 100 on every shipped table, so with no table at all the multiplier
    /// below is exactly 1.0f and the damage is bit-for-bit what it was.
    /// </remarks>
    public static int NextRate(int previousRate, DateTime lastHit, DateTime now, IReadOnlyList<int> rates)
    {
        if (rates == null || rates.Count == 0)
            return 100;

        if (previousRate <= 0)
            return rates[0];

        if ((now - lastHit).TotalSeconds > WindowSeconds)
            return rates[0];

        // The position the previous rate sat at, +1. Rates are authored descending and distinct; a rate that
        // is not in the table (a client with a different one) restarts rather than guessing a position.
        var index = -1;
        for (var i = 0; i < rates.Count; i++)
            if (rates[i] == previousRate)
            {
                index = i;
                break;
            }

        if (index < 0)
            return rates[0];

        return rates[Math.Min(index + 1, rates.Count - 1)];
    }

    /// <summary>The damage factor for a rate, as the per-cent the table authors.</summary>
    public static float RateMultiplier(int rate) => rate / 100f;
}

/// <summary>
/// The per-unit, per-skill AoE hit counter.
/// </summary>
/// <remarks>
/// Held here rather than on <see cref="Units.Unit"/> so the counter is one additive file: it is transient
/// combat state with no persistence and nothing else reads it. Entries are dropped when they decay, so a
/// unit that stops being hit stops being tracked.
/// </remarks>
public static class AoeDiminishingTracker
{
    private static readonly ConcurrentDictionary<(uint UnitObjId, uint SkillId), State> Counters = new();

    private sealed class State
    {
        public int Rate;
        public DateTime LastHit;
    }

    /// <summary>
    /// The rate the next hit of <paramref name="skillId"/> on <paramref name="unitObjId"/> takes, advancing
    /// the counter to it.
    /// </summary>
    public static int Advance(uint unitObjId, uint skillId, DateTime now, IReadOnlyList<int> rates)
    {
        var key = (unitObjId, skillId);
        var state = Counters.GetOrAdd(key, _ => new State());

        lock (state)
        {
            state.Rate = AoeDiminishingRules.NextRate(state.Rate, state.LastHit, now, rates);
            state.LastHit = now;
            return state.Rate;
        }
    }

    /// <summary>
    /// Clears the counter. <c>ResetAoeDiminishingEffect</c> is the content's way of saying the sequence is
    /// over; a null <paramref name="skillId"/> clears every skill the unit is carrying a counter for.
    /// </summary>
    public static void Reset(uint unitObjId, uint? skillId = null)
    {
        if (skillId.HasValue)
        {
            Counters.TryRemove((unitObjId, skillId.Value), out _);
            return;
        }

        foreach (var key in Counters.Keys)
            if (key.UnitObjId == unitObjId)
                Counters.TryRemove(key, out _);
    }

    /// <summary>The counter's current rate without advancing it, for tests and diagnostics.</summary>
    public static int Peek(uint unitObjId, uint skillId) =>
        Counters.TryGetValue((unitObjId, skillId), out var state) ? state.Rate : 0;

    /// <summary>How many counters are live across every unit.</summary>
    public static int TrackedCount => Counters.Count;
}
