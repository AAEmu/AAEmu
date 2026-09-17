using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots;

/// <summary>
/// How a plot condition kind is evaluated.
/// </summary>
public enum PlotConditionHandling
{
    /// <summary>Evaluated from the plot's own parameters.</summary>
    Implemented,

    /// <summary>
    /// Has an explicit arm that answers true. Kept permissive because the parameter's meaning is not
    /// established from the shipped data, so guessing would silently start blocking plots.
    /// </summary>
    Permissive,

    /// <summary>No arm at all; the value used to fall through a default case.</summary>
    Unhandled
}

/// <summary>
/// The pure decisions behind the plot condition kinds, and which kinds are handled.
/// </summary>
public static class PlotConditionRules
{
    /// <summary>
    /// The 20 kinds 10.0.2.13's <c>enum_plot_condition_kinds</c> defines, with the handling this server
    /// gives each. Kind 4 is absent from the table and from the data.
    /// </summary>
    public static readonly IReadOnlyList<(int Id, string DbName, PlotConditionHandling Handling)> DatabaseKinds =
    [
        (1, "level", PlotConditionHandling.Implemented),
        (2, "relation", PlotConditionHandling.Implemented),
        (3, "direction", PlotConditionHandling.Implemented),
        (5, "buff", PlotConditionHandling.Implemented),
        (6, "weapon_equip_status", PlotConditionHandling.Implemented),
        (7, "chance", PlotConditionHandling.Implemented),
        (8, "dead", PlotConditionHandling.Implemented),
        (9, "combat_dice_result", PlotConditionHandling.Implemented),
        (10, "instrument_type", PlotConditionHandling.Implemented),
        (11, "range", PlotConditionHandling.Implemented),
        (12, "variable", PlotConditionHandling.Implemented),
        (13, "unit_attribute", PlotConditionHandling.Implemented),
        (14, "actability", PlotConditionHandling.Implemented),
        (15, "stealth", PlotConditionHandling.Implemented),
        (16, "visible", PlotConditionHandling.Implemented),
        (17, "ab_level", PlotConditionHandling.Implemented),
        (18, "casting_useable", PlotConditionHandling.Implemented),
        (19, "combat_resource", PlotConditionHandling.Implemented),
        (20, "unit_reqs", PlotConditionHandling.Implemented),
        // 6 rows on 2 plots (6569, 6593), all three params are faction ids — the plot events are named
        // "did Nuia kill me?", "did Harihara kill me?", "is the culprit an outlaw?", so the kind asks
        // whose accumulated damage killed the plot's target. Nothing in the cast path records the faction
        // of the killing blow, so the arm stays permissive rather than answering "nobody" and sending all
        // three branches down the natural-death edge.
        (21, "accrue_damage_monster", PlotConditionHandling.Permissive)
    ];

    public static PlotConditionHandling HandlingOf(PlotConditionType kind)
    {
        foreach (var entry in DatabaseKinds)
        {
            if (entry.Id == (int)kind)
                return entry.Handling;
        }

        return PlotConditionHandling.Unhandled;
    }

    /// <summary>
    /// Plot <c>relation</c> check, resolved exactly as skill target selection resolves it (kinds 1 friendly,
    /// 3 raid, 4 hostile, 5 others, 6 friendly_for_debuff, 8 family, 10 expedition_member).
    /// </summary>
    /// <remarks>
    /// The condition used to answer from the faction relation alone and returned true for everything it did
    /// not know, so a raid or "others" gate passed unconditionally on 53 conditions across 29 skills. The
    /// area search that picks a plot's targets already filters with <see cref="SkillTargetingUtil"/>, so a
    /// condition that disagreed with it dropped units the search had just selected.
    /// </remarks>
    public static bool RelationMatches(int relationId, BaseUnit caster, BaseUnit target) =>
        SkillTargetingUtil.IsRelationValid((SkillTargetRelation)relationId, caster, target);

    /// <summary>
    /// Plot <c>buff</c> stack range: <paramref name="param3"/>..<paramref name="param4"/>.
    /// </summary>
    /// <remarks>
    /// 384 of the 10,112 buff conditions carry the range; the rest leave both at 0 and keep the old
    /// "any buff with this tag is up" answer. A 0 upper bound means "no upper bound": the data writes
    /// (1,0) beside (26,999) and (1,2), and reading 0 as "exactly zero stacks" would contradict the
    /// lower bound on the same row.
    /// </remarks>
    public static bool BuffStackInRange(int stacks, int min, int max)
    {
        if (min <= 0 && max <= 0)
            return true;
        if (min > 0 && stacks < min)
            return false;
        if (max > 0 && stacks > max)
            return false;
        return true;
    }

    /// <summary>
    /// Plot <c>casting_useable</c>: is the cast or channel the plot is running inside the
    /// <paramref name="minPercent"/>..<paramref name="maxPercent"/> band of its own duration?
    /// </summary>
    /// <remarks>
    /// The shipped bands partition the bar - plot 2557 walks (100,100), (75,99), (50,74), (25,49), (0,24)
    /// and plot 4046 walks (100,100), (81,99), (61,80), (41,60), (21,40), (0,20) - so a band is a slice of
    /// the cast, and (100,100) is the finished cast.
    ///
    /// A null <paramref name="progressPercent"/> means no cast or channel window was recorded for the plot,
    /// which is the case whenever the condition is evaluated outside a bar. That keeps the answer the
    /// condition gave before it was implemented rather than failing a gate that used to pass.
    /// </remarks>
    public static bool CastBandMatches(int? progressPercent, int minPercent, int maxPercent)
    {
        if (!progressPercent.HasValue)
            return true;
        return progressPercent.Value >= minPercent && progressPercent.Value <= maxPercent;
    }

    /// <summary>
    /// Percentage of the cast or channel window that has elapsed, saturated at 100.
    /// </summary>
    public static int CastProgressPercent(DateTime windowStartUtc, int windowMs, DateTime nowUtc)
    {
        if (windowMs <= 0)
            return 100;
        var elapsedMs = (nowUtc - windowStartUtc).TotalMilliseconds;
        if (elapsedMs <= 0)
            return 0;
        if (elapsedMs >= windowMs)
            return 100;
        return (int)(elapsedMs * 100 / windowMs);
    }
}
