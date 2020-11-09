using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Combo : SpecialEffectAction
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
            int comboSkillId,
            int timeFromNow,
            int value3,
            int value4)
        {
            _log.Warn("comboSkillId {0}, timeFromNow {1}, value3 {2}, value4 {3}", comboSkillId, timeFromNow, value3, value4);
        }
    }
}
