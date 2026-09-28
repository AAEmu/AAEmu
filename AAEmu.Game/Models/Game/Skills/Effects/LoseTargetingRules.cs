using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Which unit a targeting restriction is measured from. A buff either restricts targeting of its own
/// owner, or — when <c>buffs.targeting_use_origin_source</c> is set — of the unit that applied it.
/// </summary>
public enum TargetingRestrictionSubject
{
    /// <summary>The buff owner, which is the unit the effect landed on.</summary>
    Owner,

    /// <summary>The unit that applied the buff, used as the party the relation is measured against.</summary>
    OriginSource,
}

/// <summary>
/// One "this unit may no longer be targeted by <em>them</em>" rule read off a unit's active buffs.
/// </summary>
/// <param name="BarredRelation">
/// <see cref="SkillTargetRelation"/> the restriction names. It names the class of holder that is
/// barred, evaluated from <paramref name="RelationSubject"/> — not the class that is still allowed.
/// <see cref="SkillTargetRelation.Any"/> therefore bars every holder, and it is the only value that
/// does so.
/// </param>
/// <param name="RelationSubject">The unit the relation is measured from.</param>
/// <param name="Subject">Which of the two the relation came from, for diagnostics and tests.</param>
public sealed record TargetingRestriction(
    SkillTargetRelation BarredRelation,
    BaseUnit RelationSubject,
    TargetingRestrictionSubject Subject);

/// <summary>
/// Which units lose a target when a lose-targeting effect lands on a unit, and which of them keep it.
/// </summary>
/// <remarks>
/// <para>
/// A lose-targeting effect does two separate things to the unit it lands on. It drops that unit's own
/// current target, and — for the units that currently hold <em>it</em> as their target — it drops
/// theirs. The second half is the reversed lookup: it has to start from the affected unit and walk
/// outwards, which is why the affected unit's own targeting state is not enough to decide it.
/// </para>
/// <para>
/// The reversed half is filtered, never bulk. A holder is only selected when an active buff on the
/// affected unit actually bars that holder: <c>buffs.impossible_targeting</c> turns the buff into a
/// restriction, and <c>buffs.targeting_relation_id</c> names who it bars. A unit whose buffs bar
/// nobody loses nobody else's target, so the reversed set is empty rather than "everybody currently
/// targeting it" — clearing everybody's target on every cast is the failure mode this rule exists to
/// avoid.
/// </para>
/// <para>
/// <c>buffs.impossible_change_targeting</c> is deliberately not read. It restricts the affected unit's
/// own ability to change target, which is the other half of the effect and not a statement about who
/// else may hold it.
/// </para>
/// </remarks>
public static class LoseTargetingRules
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Resolves a <c>buffs.targeting_relation_id</c> value to its catalog relation. False for an id
    /// this build does not know, which the caller reports rather than guesses at.
    /// </summary>
    public static bool TryResolveRelation(uint relationId, out SkillTargetRelation relation)
    {
        // The catalog is byte-wide, so anything above it is not a relation id and must not be
        // clamped into range. Enum.IsDefined compares against the boxed underlying type, so the
        // candidate is narrowed first.
        if (relationId > byte.MaxValue || !Enum.IsDefined(typeof(SkillTargetRelation), (byte)relationId))
        {
            relation = default;
            return false;
        }

        relation = (SkillTargetRelation)relationId;
        return true;
    }

    /// <summary>
    /// The restrictions the affected unit's active buffs declare, one per buff that bars a relation.
    /// Empty when nothing on it bars anybody, which is the common case and means the reversed
    /// selection selects nobody.
    /// </summary>
    public static IReadOnlyList<TargetingRestriction> CollectRestrictions(BaseUnit targeted)
    {
        if (targeted?.Buffs == null)
            return [];

        var restrictions = new List<TargetingRestriction>();
        foreach (var buff in targeted.Buffs.GetEffectsMatchingCondition(
                     static buff => buff?.Template is { ImpossibleTargeting: true }))
        {
            var relationId = buff.Template.TargetingRelationId;
            if (!TryResolveRelation(relationId, out var relation))
            {
                // Skipped loudly. Reading it as "any" here would bar every holder of the affected
                // unit, which is the opposite of what an unreadable id authorises.
                Logger.Error(
                    "lose_targeting: buff {0} on unit {1} bars targeting with relation id {2}, which is " +
                    "not a targeting relation; its restriction is skipped",
                    buff.Template.Id, targeted.ObjId, relationId);
                continue;
            }

            var fromOriginSource = buff.Template.TargetingUseOriginSource && buff.Caster != null;
            restrictions.Add(new TargetingRestriction(
                relation,
                fromOriginSource ? buff.Caster : targeted,
                fromOriginSource ? TargetingRestrictionSubject.OriginSource : TargetingRestrictionSubject.Owner));
        }

        return restrictions;
    }

    /// <summary>
    /// Whether <paramref name="restriction"/> bars <paramref name="holder"/> from holding the
    /// affected unit as its current target.
    /// </summary>
    public static bool Bars(TargetingRestriction restriction, BaseUnit holder)
    {
        ArgumentNullException.ThrowIfNull(restriction);

        if (holder == null)
            return false;

        // A rule whose subject could not be resolved cannot be measured. It bars nobody rather than
        // everybody, so a missing origin source narrows the selection instead of widening it.
        if (restriction.RelationSubject == null)
            return false;

        if (restriction.BarredRelation == SkillTargetRelation.Any)
            return true;

        return SkillTargetingUtil.IsRelationValid(
            restriction.BarredRelation, restriction.RelationSubject, holder);
    }

    /// <summary>
    /// The units that must lose their target because the affected unit can no longer be targeted by
    /// them: the candidates currently holding the affected unit as their target, minus the ones no
    /// restriction bars.
    /// </summary>
    /// <param name="targeted">The unit the effect landed on.</param>
    /// <param name="candidates">
    /// The units to consider — the ones this World instance holds. Null means the instance does not
    /// know the affected unit, and nothing can be enumerated.
    /// </param>
    public static IReadOnlyList<Unit> SelectHolders(BaseUnit targeted, IEnumerable<Unit> candidates)
    {
        ArgumentNullException.ThrowIfNull(targeted);

        if (candidates == null)
        {
            Logger.Debug("lose_targeting: unit {0} is in no known world instance, no holder can be enumerated",
                targeted.ObjId);
            return [];
        }

        var restrictions = CollectRestrictions(targeted);
        if (restrictions.Count == 0)
            return [];

        var selected = new List<Unit>();
        foreach (var candidate in candidates)
        {
            // The unit itself is never one of its own holders; the effect drops its own target on the
            // direct path instead.
            if (candidate == null || candidate.ObjId == targeted.ObjId)
                continue;

            if (candidate.CurrentTarget == null || candidate.CurrentTarget.ObjId != targeted.ObjId)
                continue;

            if (restrictions.Any(restriction => Bars(restriction, candidate)))
                selected.Add(candidate);
        }

        return selected;
    }
}
