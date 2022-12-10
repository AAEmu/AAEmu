using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class DisturbCasting : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.DisturbCasting;
        
        // Parameters are estimated to be :
        // value1 = chance ?
        // value2 = delay ?
        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int chance, int delay, int value3, int value4)
        {
            if (caster is Character) { _log.Debug("Special effects: DisturbCasting chance {0}, delay {1}, value3 {2}, value4 {3}", chance, delay, value3, value4); }
            
            if (target is Unit unit)
            {
                unit.ActivePlotState?.RequestCancellation();
                // TODO: Find a way to cancel normal skills properly
            }
        }
    }
}
