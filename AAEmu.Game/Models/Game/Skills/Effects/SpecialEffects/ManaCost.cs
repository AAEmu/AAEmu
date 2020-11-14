using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ManaCost : SpecialEffectAction
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
            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

            if (caster is Character character)
            {
                _log.Debug("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
                var manaCost = character.Modifiers.ApplyModifiers(skill, SkillAttribute.ManaCost, value2);
                character.ReduceCurrentMp(null, (int)manaCost);
                // TODO / 10
            }
        }
    }
}
