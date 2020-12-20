using System;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public abstract class BaseCombatBehavior : Behavior
    {
        protected DateTime _delayEnd;
        protected bool _strafeDuringDelay;
        
        public void MoveInRange(BaseUnit target, float range, float speed)
        {
            var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Position, target.Position, true);
            if (distanceToTarget > range)
                Ai.Owner.MoveTowards(target.Position, speed);
        }

        protected bool CanStrafe 
        {
            get
            {
                return DateTime.UtcNow > _delayEnd || _strafeDuringDelay;
            }
        }
        
        protected bool CanUseSkill
        {
            get
            {
                if (Ai.Owner.SkillTask != null || Ai.Owner.ActivePlotState != null)
                    return false;
                return DateTime.UtcNow >= _delayEnd;
            }
        }
        
        // UseSkill (delay)
        public void UseSkill(Skill skill, BaseUnit target)
        {
            
        }
        
        // Check if can pick a new skill (delay, already casting)
    }
}
