using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ManaCost : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.ManaCost;
        
        public override void Execute(Unit caster,
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
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: ManaCost value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            if (caster is Character character)
            {
                // TODO: Value1 is used by Mana Stars, value2 is used by other skills. They are never used both at once.
                // I think value1 is fixed, and value2 is based on skill level somehow.
                var manaCost = character.SkillModifiersCache.ApplyModifiers(skill, SkillAttribute.ManaCost, value1 + (value2)/6.35);
                character.ReduceCurrentMp(null, (int)manaCost);
                
                character.LastCast = DateTime.UtcNow;
                character.IsInPostCast = true;
                // TODO / 10
            }
        }
    }
}
