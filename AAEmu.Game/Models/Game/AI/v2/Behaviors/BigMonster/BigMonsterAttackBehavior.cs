using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.AI.v2.Params.BigMonster;
using AAEmu.Game.Models.Game.AI.V2.Params.BigMonster;
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
            
            if (CanStrafe)
                MoveInRange(target, Ai.Owner.Template.AttackStartRangeScale * Ai.Owner.ModelSize, 5.4f * (delta.Milliseconds / 1000.0f));

            if (!CanUseSkill)
                return;

            #region Pick a skill
            // TODO: Get skill list
            // If skill list is empty, get Base skill
            #endregion

            UseSkill(null, Ai.Owner.CurrentTarget);
        }

        private List<BigMonsterCombatSkill> RequestAvailableSkills(BigMonsterAiParams aiParams)
        {
            var healthRatio = (Ai.Owner.Hp / Ai.Owner.MaxHp) * 100;
            
            var baseList = aiParams.CombatSkills.AsEnumerable();

            baseList = baseList.Where(s => s.HealthRangeMin <= healthRatio && healthRatio <= s.HealthRangeMax);
            baseList = baseList.Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillType));

            return baseList.ToList();
        }
        
        public override void Exit()
        {
            // Clear combat state here
        }
    }
}
