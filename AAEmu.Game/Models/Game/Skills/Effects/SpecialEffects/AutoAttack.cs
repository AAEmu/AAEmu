using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AutoAttack : SpecialEffectAction
{
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
        if (caster is not Character character)
            return;

        if (character.IsAutoAttack)
            return;

        // value1 = skillId to auto-attack with (2=melee, 3=offhand, 4=ranged), 0 = melee default
        var attackSkillId = (uint)(value1 > 0 ? value1 : 2);
        var attackTemplate = SkillManager.Instance.GetSkillTemplate(attackSkillId);
        if (attackTemplate == null)
            return;

        var attackSkill = new Skill(attackTemplate);
        character.IsAutoAttack = true;
        character.StartAutoSkill(attackSkill);
    }
}
