using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// 157 change_buff_tolerance_step: moves a crowd-control tolerance family onto another rung.
/// </summary>
/// <remarks>
/// The target is the caster's own ladder — the effect carries no target selector, and the one shipped
/// row that a skill reaches is 39373 on 40364 결투를 위하여, a self-cast that heals, restores mana, resets
/// cooldown tags 378/904/2065 and clears a debuff tag. Values are read as (family, rung); see
/// <see cref="BuffToleranceStepRules"/> for the evidence, for what a zero family means and for why the
/// two are not distinguishable from shipped content.
/// </remarks>
public class ChangeBuffToleranceStep : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ChangeBuffToleranceStep;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Unit unit)
            return;

        var moved = unit.Buffs.SetToleranceStep(value1, value2);
        if (moved == 0)
            Logger.Debug(
                "change_buff_tolerance_step: {0} had no counter for family {1} (rung {2})",
                unit.Name,
                value1,
                value2);
    }
}
