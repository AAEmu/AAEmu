﻿using System;
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
            
            var target = Ai.Owner.CurrentTarget;
            if (target == null)
                return; // Technically, the aggro code should take us out of this state very soon.
            
            if (CanStrafe && !IsUsingSkill)
                MoveInRange(target, Ai.Owner.Template.AttackStartRangeScale * 4, 5.4f * (delta.Milliseconds / 1000.0f));

            if (!CanUseSkill)
                return;

            _strafeDuringDelay = false;
            #region Pick a skill
            // TODO: Get skill list
            var selectedSkill = PickSkill(RequestAvailableSkills(aiParams));
            if (selectedSkill == null)
                return;
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill.SkillType);

            if (skillTemplate == null)
                return;

            var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
            if (targetDist >= skillTemplate.MinRange && targetDist <= skillTemplate.MaxRange)
            {
                Ai.Owner.StopMovement();
                UseSkill(new Skill(skillTemplate), target, selectedSkill.SkillDelay);
                _strafeDuringDelay = selectedSkill.StrafeDuringDelay;
            }
            // If skill list is empty, get Base skill
            #endregion
        }

        private List<BigMonsterCombatSkill> RequestAvailableSkills(BigMonsterAiParams aiParams)
        {
            int healthRatio = (int)(((float)Ai.Owner.Hp / Ai.Owner.MaxHp) * 100);
            
            var baseList = aiParams.CombatSkills.AsEnumerable();

            baseList = baseList.Where(s => s.HealthRangeMin <= healthRatio && healthRatio <= s.HealthRangeMax);
            baseList = baseList.Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillType));

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
