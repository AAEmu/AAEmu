using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ProtectionForExpedition : SpecialEffectAction
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
        if (caster is not Character character || value1 <= 0)
            return;

        ExpeditionManager.Instance.SetProtection(character, value1);
    }
}
