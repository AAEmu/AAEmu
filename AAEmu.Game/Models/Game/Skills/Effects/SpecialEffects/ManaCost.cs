using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ManaCost : SpecialEffectAction
    {
        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3, int value4)
        {
            if (caster is Character character)
            {
                var manaCost = character.Modifiers.ApplyModifiers(skill, SkillAttribute.ManaCost, value2); 
                character.ReduceCurrentMp(null, (int)manaCost);
                // TODO / 10
            }
        }
    }
}
