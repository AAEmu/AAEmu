using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.AI.v2.Params.BigMonster;
using AAEmu.Game.Models.Game.AI.V2.Params.BigMonster;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.BigMonster
{
    public class BigMonsterAttackBehavior : BaseCombatBehavior
    {
        public override void Enter()
        {
            Ai.Owner.InterruptSkills();
        }

        public override void Tick(TimeSpan delta)
        {
            var aiParams = Ai.Owner.Template.AiParams as BigMonsterAiParams;
            if (aiParams == null)
                return;

            if (!UpdateTarget() || ShouldReturn)
            {
                Ai.GoToReturn();
                return;
            }
            
            if (CanStrafe && !IsUsingSkill)
                MoveInRange(Ai.Owner.CurrentTarget, delta);

            if (!CanUseSkill)
                return;

            _strafeDuringDelay = false;
            #region Pick a skill
            // TODO: Get skill list
            var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
            var selectedSkill = PickSkill(RequestAvailableSkills(aiParams, targetDist));
            if (selectedSkill == null)
                return;
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill.SkillType);

            if (skillTemplate == null)
                return;

            if (targetDist >= skillTemplate.MinRange && targetDist <= skillTemplate.MaxRange || skillTemplate.TargetType == SkillTargetType.Self)
            {
                Ai.Owner.StopMovement();
                UseSkill(new Skill(skillTemplate), Ai.Owner.CurrentTarget, selectedSkill.SkillDelay);
                _strafeDuringDelay = selectedSkill.StrafeDuringDelay;
            }
            // If skill list is empty, get Base skill
            #endregion
        }

        private List<BigMonsterCombatSkill> RequestAvailableSkills(BigMonsterAiParams aiParams, float trgDist)
        {
            int healthRatio = (int)(((float)Ai.Owner.Hp / Ai.Owner.MaxHp) * 100);
            
            var baseList = aiParams.CombatSkills.AsEnumerable();

            baseList = baseList.Where(s => s.HealthRangeMin <= healthRatio && healthRatio <= s.HealthRangeMax);
            baseList = baseList.Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillType));
            baseList = baseList.Where(s =>
            {
                var template = SkillManager.Instance.GetSkillTemplate(s.SkillType);
                return (template != null && (trgDist >= template.MinRange && trgDist <= template.MaxRange || template.TargetType == SkillTargetType.Self));
            });

            return baseList.ToList();
        }

        private BigMonsterCombatSkill PickSkill(List<BigMonsterCombatSkill> skills)
        {
            if (skills.Count > 0)
                return skills[Rand.Next(0, skills.Count)];
            
            if (!Ai.Owner.Cooldowns.CheckCooldown((uint) Ai.Owner.Template.BaseSkillId))
                return new BigMonsterCombatSkill
                {
                    SkillType = (uint)Ai.Owner.Template.BaseSkillId,
                    SkillDelay = Ai.Owner.Template.BaseSkillDelay,
                    StrafeDuringDelay = Ai.Owner.Template.BaseSkillStrafe
                };

            return null;
        }
        
        public override void Exit()
        {
            // Clear combat state here
        }
    }
}
