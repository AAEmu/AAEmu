using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Lift seats apply a Timeout trigger whose effect is a SpecialEffect (the floor-move ride).
/// Beds apply a Timeout trigger whose effect is a BuffEffect (sleep). Only the lift path
/// should fire on unbond — standing up from a bed must not start the sleep trigger.
/// </summary>
public static class SeatRideRules
{
    public static bool ShouldTimeoutOnUnbond(IEnumerable<BuffTriggerTemplate> triggers)
    {
        if (triggers == null)
            return false;

        foreach (var trigger in triggers)
        {
            if (trigger is { Kind: BuffEventTriggerKind.Timeout, Effect: SpecialEffect })
                return true;
        }

        return false;
    }

    public static bool ShouldTimeoutOnUnbond(
        SkillTemplate skill,
        Func<uint, IEnumerable<BuffTriggerTemplate>> triggersForBuff)
    {
        if (skill?.Effects == null || triggersForBuff == null)
            return false;

        foreach (var effect in skill.Effects)
        {
            if (effect?.Template is not BuffEffect buffEffect || buffEffect.Buff == null)
                continue;

            if (ShouldTimeoutOnUnbond(triggersForBuff(buffEffect.Buff.Id)))
                return true;
        }

        return false;
    }

    public static bool ShouldTimeoutOnUnbond(uint sourceSkillId)
    {
        if (sourceSkillId == 0)
            return false;

        var skills = SkillManager.Instance;
        if (skills == null)
            return false;

        return ShouldTimeoutOnUnbond(skills.GetSkillTemplate(sourceSkillId), skills.GetBuffTriggerTemplates);
    }
}
