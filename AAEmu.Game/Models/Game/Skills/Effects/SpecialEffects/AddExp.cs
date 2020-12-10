using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class AddExp : SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

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
            if (!(target is Unit unit))
                return;
            var expToAdd = value1;

            if (expToAdd == 0 && unit.Level >= 50) // Experia
            {
                var expBySkillEffectForLevel = FormulaManager.Instance.GetFormula((uint)FormulaKind.ExpBySkillEffect);
                var res = expBySkillEffectForLevel.Evaluate(new Dictionary<string, double>() { ["pc_level"] = unit.Level});

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
