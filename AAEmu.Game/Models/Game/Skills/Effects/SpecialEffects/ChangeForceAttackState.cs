using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public sealed class ChangeForceAttackState : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ChangeForceAttackState;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int enabled, int value2, int value3,
        int value4)
    {
        // The client casts this skill (자유 공격, 50452) once per Ctrl+F press. The shipped effect row
        // (type change_force_attack_state) carries no direction - every value is 0 - so the cast
        // toggles free-attack mode instead of setting it. Treating value1 as "enabled" always read 0
        // and turned the mode off on every press, so it could never be switched on.
        if (target is Unit unit)
        {
            unit.SetForceAttack(!unit.ForceAttack);
        }
    }
}
