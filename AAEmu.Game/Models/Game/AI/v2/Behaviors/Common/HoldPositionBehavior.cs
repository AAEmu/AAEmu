using System;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

using NLog;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class HoldPositionBehavior : BaseCombatBehavior
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override void Enter()
        {
        }
        public override void Tick(TimeSpan delta)
        {
            PickSkillAndUseIt();
        }

        private void PickSkillAndUseIt()
        {
            if (!Ai.Owner.Template.Skills.ContainsKey(SkillUseConditionKind.InIdle)) { return; }
            var skills = Ai.Owner.Template.Skills[SkillUseConditionKind.InIdle];
            skills = skills
                .Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillId))
                .Where(s =>
                {
                    var template = SkillManager.Instance.GetSkillTemplate(s.SkillId);
                    return template is {TargetType: SkillTargetType.Self};
                }).ToList();
            if (skills.Count <= 0) { return; }
            var npSkillId = skills[Rand.Next(skills.Count)].SkillId;
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(npSkillId);
            if (Ai.Owner.Cooldowns.CheckCooldown(skillTemplate.Id)) { return; }
            Ai.Owner.CurrentTarget = Ai.Owner; // target to self
            var skill = new Skill(skillTemplate);
            _log.Warn("PickSkillAndUseIt:UseSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplate.Id);
            UseSkill(skill, Ai.Owner.CurrentTarget, Ai.Owner.Template.BaseSkillDelay);
        }

        public override void Exit()
        {
        }
    }
}
