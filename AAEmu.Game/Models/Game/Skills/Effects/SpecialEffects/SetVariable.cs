using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class SetVariable : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.SetVariable;

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
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: SetVariable index {0}, value {1}, operation {2}, value4 {3}", value1, value2, value3, value4); }

            var index = value1;
            var value = value2;
            var operation = value3;
            //value 4 unused


            //There is a high chance this is not implemented correctly..
            //If refactoring. See PlotConditions -> Variable as well
            if (skill.ActivePlotState != null)
            {
                if (operation == 1)
                {
                    skill.ActivePlotState.Variables[index] += value;
                }
                else if (operation == 11)
                {
                    skill.ActivePlotState.Variables[index] = value;
                }
                else
                {
                    _log.Error("Invalid Plot Variable Operation Kind.");
                }
            }
            else
            {
                _log.Error("No active plot state located.");
            }

            _log.Trace("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
        }
    }
}
