using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public sealed class ChangeForceAttackState : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ChangeForceAttackState;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int enabled, int value2, int value3,
        int value4)
    {
        if (target is Unit unit)
        {
            unit.SetForceAttack(enabled != 0);
        }
    }
}
