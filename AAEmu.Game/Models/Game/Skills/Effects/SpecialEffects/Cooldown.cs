using System;

using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Cooldown : SpecialEffectAction
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
            int cooldownTime,
            int value2,
            int value3,
            int value4)
        {
            // TODO only for server
            _log.Warn("cooldownTime {0}, value2 {1}, value3 {2}, value4 {3}", cooldownTime, value2, value3, value4);
        }
    }
}
