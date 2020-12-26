using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class AttackBehavior : BaseCombatBehavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            if (!UpdateTarget() || ShouldReturn)
            {
                Ai.GoToReturn();
                return;
            }

            MoveInRange(Ai.Owner.CurrentTarget, delta);
            if (!CanUseSkill)
                return;
            var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
            PickSkillAndUseIt(targetDist);
        }

        private void PickSkillAndUseIt(float trgDist)
        {
            // Attack behavior probably only uses base skill ?
            var skills = new List<NpcSkill>();
            if (Ai.Owner.Template.Skills.ContainsKey(SkillUseConditionKind.InCombat))
                skills = Ai.Owner.Template.Skills[SkillUseConditionKind.InCombat];
            skills = skills
                .Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillId))
                .Where(s =>
                {
                    var template = SkillManager.Instance.GetSkillTemplate(s.SkillId);
                    return (template != null && (trgDist >= template.MinRange && trgDist <= template.MaxRange || template.TargetType == SkillTargetType.Self));
                }).ToList();

            var pickedSkillId = (uint)Ai.Owner.Template.BaseSkillId;
            if (skills.Count > 0)
                pickedSkillId = skills[Rand.Next(skills.Count)].SkillId;
            // Hackfix for Melee attack. Needs to look at the held weapon (if any) or default to 3m. 
            if (pickedSkillId == 2 && trgDist > 4.0f)
                return;
            
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(pickedSkillId);
            var skill = new Skill(skillTemplate);

            if (!Ai.Owner.Cooldowns.CheckCooldown(skillTemplate.Id))
                UseSkill(skill, Ai.Owner.CurrentTarget, Ai.Owner.Template.BaseSkillDelay);
        }

        public override void Exit()
        {
        }
    }
}
