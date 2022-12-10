using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class AddExp : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.AddExp;
        
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
            if (caster is Character) { _log.Debug("Special effects: AddExp value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            if (!(target is Unit unit))
            {
                return;
            }

            var expToAdd = value1;

            if (expToAdd == 0 && unit.Level >= 50) // Experia
            {
                var expBySkillEffectForLevel = FormulaManager.Instance.GetFormula((uint)FormulaKind.ExpBySkillEffect);
                var res = expBySkillEffectForLevel.Evaluate(new Dictionary<string, double>() { ["pc_level"] = unit.Level });

                expToAdd = (int)(res * (value3 / 10.0f));
            }

            switch (target)
            {
                case Units.Mate mate:
                    mate.AddExp(expToAdd);
                    break;
                case Character character:
                    character.AddExp(expToAdd, true);
                    break;
            }
        }
    }
}
