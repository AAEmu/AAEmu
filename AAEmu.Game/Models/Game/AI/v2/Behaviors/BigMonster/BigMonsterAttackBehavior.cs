using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.BigMonster
{
    public class BigMonsterAttackBehavior : Behavior
    {
        public override void Enter()
        {
            Ai.Owner.InterruptSkills();
        }

        public override void Tick(TimeSpan delta)
        {
            // Note: The regions used here can probably be moved to functions
            #region Move to target
            // TODO: Check that strafe = true OR that we are not in a delay
            // Get target
            var target = Ai.Owner.CurrentTarget;
            if (target == null)
                return; // Technically, the aggro code should take us out of this state very soon.

            // Get in preferred combat range OR melee attack range, not sure which yet.
            var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Position, target.Position, true);
            if (distanceToTarget > Ai.Param.MeleeAttackRange)
                Ai.Owner.MoveTowards(target.Position, 2.4f * (delta.Milliseconds / 1000.0f));
            #endregion

            #region Can cast next skill check

            // TODO: Check we aren't currently in a delay state

            // TODO: Check we aren't already casting/"active"

            #endregion
            
            // Pick a skill to use

            #region Pick a skill

            // TODO: Get skill list
            // If skill list is empty, get Base skill

            #endregion

            #region Use skill

            

            #endregion
            
            // Use that skill

            // Apply that skill's delay 
        }

        private List<AiSkill> RequestAvailableSkills()
        {
            var healthRatio = (Ai.Owner.Hp / Ai.Owner.MaxHp) * 100;
            
            var baseList = Ai.Param.BigMonsterCombatSkills.AsEnumerable();

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
