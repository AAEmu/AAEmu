using System;
using AAEmu.Commons.Utils;
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
            int chance,
            int value4)
        {
            if (Rand.Next(0, 100) > chance && chance != 0)
                return;
            target = caster.CurrentTarget;
            var useSkill = new Skill(SkillManager.Instance.GetSkillTemplate((uint)skillId));
            targetObj = new SkillCastUnitTarget(target?.ObjId ?? 0);
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.UseSkill);//Not sure if it belongs here.
            TaskManager.Instance.Schedule(new UseSkillTask(useSkill, caster, casterObj, target, targetObj, skillObject), TimeSpan.FromMilliseconds(delay));
            //useSkill.ApplyEffects(caster, casterObj, target, targetObj, skillObject);
            _log.Warn("SkillId {0}, Delay {1}, Chance {2}, value4 {3}", skillId, delay, chance, value4);
        }
    }
}
