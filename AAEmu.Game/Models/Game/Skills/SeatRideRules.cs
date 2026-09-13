using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Decides whether standing up from a seat must run the seat's timeout trigger. Only a ride does: the
/// timeout trigger has to lead to a move (a Blink of the rider). A seat whose trigger casts a reward
/// skill (a meditation cushion) or applies a buff (a bed) keeps its natural timing instead.
/// </summary>
/// <remarks>
/// The ride is a chain, not a single effect: the lift seat's timeout casts a skill, that skill applies a
/// buff whose own trigger applies a second buff, and that buff's Started trigger performs the Blink. The
/// walk below follows skill and buff hops until it finds the move or runs out of depth.
/// </remarks>
public static class SeatRideRules
{
    /// <summary>How many skill/buff hops the ride search follows before giving up.</summary>
    public const int MaxRideDepth = 5;

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
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff)
    {
        if (triggers == null)
            return false;

        foreach (var trigger in triggers)
        {
            if (trigger is { Kind: BuffEventTriggerKind.Timeout, Effect: SpecialEffect effect } &&
                PerformsRide(effect, skillResolver, triggersForBuff, MaxRideDepth))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ride test for a whole skill: the move must be reachable from the buffs the skill applies.
    /// </summary>
    public static bool ShouldTimeoutOnUnbond(
        SkillTemplate skill,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff)
    {
        if (skill?.Effects == null)
            return false;

        foreach (var effect in skill.Effects)
        {
            if (effect?.Template is not BuffEffect buffEffect || buffEffect.Buff == null)
                continue;

            if (FollowsRide(buffEffect.Buff.Id, skillResolver, triggersForBuff, MaxRideDepth))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The production entry point: the skill a seat's doodad function applied at bond time.
    /// </summary>
    public static bool ShouldTimeoutOnUnbond(uint sourceSkillId)
    {
        if (sourceSkillId == 0 || SkillManager.Instance == null)
            return false;

        return ShouldTimeoutOnUnbond(sourceSkillId, SkillManager.Instance.GetSkillTemplate,
            SkillManager.Instance.GetBuffTriggerTemplates);
    }

    /// <summary>The same walk with the caller supplying the skill and trigger lookups.</summary>
    public static bool ShouldTimeoutOnUnbond(
        uint sourceSkillId,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff)
    {
        if (sourceSkillId == 0 || skillResolver == null)
            return false;

        var skill = skillResolver(sourceSkillId);
        return skill != null && ShouldTimeoutOnUnbond(skill, skillResolver, triggersForBuff);
    }

    private static bool PerformsRide(
        SpecialEffect effect,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
        int depth)
    {
        if (effect == null || depth <= 0)
            return false;

        if (IsMove(effect.SpecialEffectTypeId))
            return true;

        if (effect.SpecialEffectTypeId != SpecialType.SkillUse || skillResolver == null)
            return false;

        var skill = skillResolver((uint)effect.Value1);
        if (skill?.Effects == null)
            return false;

        foreach (var skillEffect in skill.Effects)
        {
            if (skillEffect?.Template is not BuffEffect buffEffect || buffEffect.Buff == null)
                continue;

            if (FollowsRide(buffEffect.Buff.Id, skillResolver, triggersForBuff, depth - 1))
                return true;
        }

        return false;
    }

    private static bool FollowsRide(
        uint buffId,
        Func<uint, SkillTemplate> skillResolver,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff,
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
                case SpecialEffect effect when PerformsRide(effect, skillResolver, triggersForBuff, depth - 1):
                    return true;
                case BuffEffect buffEffect when buffEffect.Buff != null &&
                                                FollowsRide(buffEffect.Buff.Id, skillResolver, triggersForBuff,
                                                    depth - 1):
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
