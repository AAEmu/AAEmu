using System;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ItemGradeEnchanting : SpecialEffectAction
    {
        private enum GradeEnchantResult
        {
            Break = 0,
            Downgrade = 1,
            Fail = 2,
            Success = 3,
            GreatSuccess = 4
        }
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
            _log.Trace("Special effects: ItemGradeEnchanting");
        }
    }
}
