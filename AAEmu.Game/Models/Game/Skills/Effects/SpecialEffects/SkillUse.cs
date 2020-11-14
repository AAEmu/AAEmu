using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class SkillUse : SpecialEffectAction
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
            int skillId,
            int delay,
            int value3,
            int value4)
        {
            var useSkill = new Skill(SkillManager.Instance.GetSkillTemplate((uint)skillId));
            TaskManager.Instance.Schedule(new UseSkillTask(useSkill, caster, casterObj, target, targetObj, skillObject), TimeSpan.FromMilliseconds(delay));
            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", skillId, delay, value3, value4);
        }
    }
}
