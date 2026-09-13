using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Decides whether standing up from a seat must run the seat's timeout trigger. Only a ride does: the
/// timeout trigger has to lead to a move (a Blink of the rider). A seat whose trigger casts a reward
/// skill (a meditation cushion) or applies a buff (a bed) keeps its natural timing instead.
/// </summary>
/// <remarks>
/// The ride is a chain, not a single effect. One lift walks timeout -> skill -> buff -> buff -> Started ->
/// Blink; the other walks timeout -> skill -> buff -> buff -> a plot skill whose plot start event carries
/// the Blink (<c>skills.plot_id</c> -> <c>plot_effects</c>). The walk follows skill, buff and plot hops
/// until it finds the move or runs out of depth.
/// </remarks>
public static class SeatRideRules
{
    /// <summary>How many skill/buff/plot hops the ride search follows before giving up.</summary>
    public const int MaxRideDepth = 8;

    public static bool ShouldTimeoutOnUnbond(IEnumerable<BuffTriggerTemplate> triggers)
    {
        if (triggers == null)
            return false;

        foreach (var trigger in triggers)
        {
            if (trigger is { Kind: BuffEventTriggerKind.Timeout, Effect: SpecialEffect effect } &&
                IsMove(effect.SpecialEffectTypeId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The seat's own trigger is a timeout; the chain it starts has to reach a move.
    /// </summary>
    public static bool ShouldTimeoutOnUnbond(
        IEnumerable<BuffTriggerTemplate> triggers,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver = null)
    {
        if (triggers == null)
            return false;

        foreach (var trigger in triggers)
        {
            if (trigger is { Kind: BuffEventTriggerKind.Timeout, Effect: SpecialEffect effect } &&
                PerformsRide(effect, skillResolver, triggersForBuff, plotEffectsResolver, MaxRideDepth))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ride test for a whole skill: the move must be reachable from the buffs the skill applies or from
    /// the plot it runs.
    /// </summary>
    public static bool ShouldTimeoutOnUnbond(
        SkillTemplate skill,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver = null)
    {
        if (skill == null)
            return false;

        return FollowsSkill(skill, skillResolver, triggersForBuff, plotEffectsResolver, MaxRideDepth);
    }

    /// <summary>
    /// The production entry point: the skill a seat's doodad function applied at bond time.
    /// </summary>
    public static bool ShouldTimeoutOnUnbond(uint sourceSkillId)
    {
        if (sourceSkillId == 0 || SkillManager.Instance == null)
            return false;

        return ShouldTimeoutOnUnbond(sourceSkillId, SkillManager.Instance.GetSkillTemplate,
            SkillManager.Instance.GetBuffTriggerTemplates, PlotEffects);
    }

    /// <summary>The same walk with the caller supplying the skill and trigger lookups.</summary>
    public static bool ShouldTimeoutOnUnbond(
        uint sourceSkillId,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver = null)
    {
        if (sourceSkillId == 0 || skillResolver == null)
            return false;

        var skill = skillResolver(sourceSkillId);
        return skill != null && ShouldTimeoutOnUnbond(skill, skillResolver, triggersForBuff, plotEffectsResolver);
    }

    /// <summary>
    /// The effect templates a plot runs when it starts: the start event's plot_effects, resolved against
    /// the same effect registry every other template comes from.
    /// </summary>
    private static IEnumerable<EffectTemplate> PlotEffects(uint plotId)
    {
        var plot = PlotManager.Instance?.GetPlot(plotId);
        var effects = plot?.EventTemplate?.Effects;
        if (effects == null || effects.Count == 0)
            return [];

        var manager = SkillManager.Instance;
        if (manager == null)
            return [];

        return effects
            .Select(effect => manager.GetEffectTemplate(effect.ActualId, effect.ActualType))
            .Where(template => template != null);
    }

    private static bool PerformsRide(
        SpecialEffect effect,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver,
        int depth)
    {
        if (effect == null || depth <= 0)
            return false;

        if (IsMove(effect.SpecialEffectTypeId))
            return true;

        if (effect.SpecialEffectTypeId != SpecialType.SkillUse || skillResolver == null)
            return false;

        var skill = skillResolver((uint)effect.Value1);
        return skill != null && FollowsSkill(skill, skillResolver, triggersForBuff, plotEffectsResolver, depth - 1);
    }

    /// <summary>
    /// Everything a skill can ride through: the buffs it applies and the plot it runs.
    /// </summary>
    private static bool FollowsSkill(
        SkillTemplate skill,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver,
        int depth)
    {
        if (skill == null || depth <= 0)
            return false;

        foreach (var effect in skill.Effects ?? [])
        {
            if (effect?.Template is not BuffEffect buffEffect || buffEffect.Buff == null)
                continue;

            if (FollowsRide(buffEffect.Buff.Id, skillResolver, triggersForBuff, plotEffectsResolver, depth - 1))
                return true;
        }

        // The floor mover's second shape rides through a plot: skills.plot_id -> the plot's start event
        // carries the Blink, and there are no skill effects to follow.
        return skill.Plot != null &&
               FollowsPlot(skill.Plot.Id, skillResolver, triggersForBuff, plotEffectsResolver, depth - 1);
    }

    private static bool FollowsRide(
        uint buffId,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver,
        int depth)
    {
        if (depth <= 0 || triggersForBuff == null)
            return false;

        var triggers = triggersForBuff(buffId);
        if (triggers == null)
            return false;

        foreach (var trigger in triggers)
        {
            switch (trigger?.Effect)
            {
                case SpecialEffect effect when PerformsRide(effect, skillResolver, triggersForBuff,
                    plotEffectsResolver, depth - 1):
                    return true;
                case BuffEffect buffEffect when buffEffect.Buff != null &&
                                                FollowsRide(buffEffect.Buff.Id, skillResolver, triggersForBuff,
                                                    plotEffectsResolver, depth - 1):
                    return true;
            }
        }

        return false;
    }

    private static bool FollowsPlot(
        uint plotId,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        Func<uint, IEnumerable<EffectTemplate>> plotEffectsResolver,
        int depth)
    {
        if (depth <= 0 || plotEffectsResolver == null)
            return false;

        var effects = plotEffectsResolver(plotId);
        if (effects == null)
            return false;

        foreach (var effect in effects)
        {
            switch (effect)
            {
                case SpecialEffect special when PerformsRide(special, skillResolver, triggersForBuff,
                    plotEffectsResolver, depth - 1):
                    return true;
                case BuffEffect buffEffect when buffEffect.Buff != null &&
                                                FollowsRide(buffEffect.Buff.Id, skillResolver, triggersForBuff,
                                                    plotEffectsResolver, depth - 1):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Special effects that move the rider. Blink is the floor mover's hop; another ride mechanism
    /// belongs in this set once its data appears.
    /// </summary>
    private static bool IsMove(SpecialType type) => type == SpecialType.Blink;
}
