using System;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class CancelStealth : SpecialEffectAction
    {
        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3, int value4)
        {
            caster.Effects.RemoveStealth();
            // TODO: add to server
        }
    }
}
