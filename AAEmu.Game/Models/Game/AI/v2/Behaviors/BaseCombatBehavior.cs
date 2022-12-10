using System;
using System.Linq;

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using static AAEmu.Game.Models.Game.Skills.SkillControllers.SkillController;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public abstract class BaseCombatBehavior : Behavior
    {
        protected bool _strafeDuringDelay;

        public void MoveInRange(BaseUnit target, TimeSpan delta)
        {
            if (Ai.Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep))
                return;
            if ((Ai.Owner?.ActiveSkillController?.State ?? SCState.Ended) == SCState.Running)
                return;

            //Ai.Owner.Template.AttackStartRangeScale * 4, 
            var range = 2f;// Ai.Owner.Template.AttackStartRangeScale * 6;
            var speed = 5.4f * (delta.Milliseconds / 1000.0f);
            var distanceToTarget = Ai.Owner.GetDistanceTo(target, true);
            // var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Position, target.Position, true);
            if (distanceToTarget > range)
                Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
            else
                Ai.Owner.StopMovement();
        }

        protected bool CanStrafe
        {
            get
            {
                return DateTime.UtcNow > _delayEnd || _strafeDuringDelay;
            }
        }

        protected bool IsUsingSkill
        {
            get
            {
                return Ai.Owner.SkillTask != null || Ai.Owner.ActivePlotState != null;
            }
        }

        protected bool CanUseSkill
        {
            get
            {
                if (IsUsingSkill)
                    return false;
                if ((Ai.Owner?.ActiveSkillController?.State ?? SCState.Ended) == SCState.Running)
                    return false;
                if (Ai.Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep || e.Template.Silence))
                    return false;
                return DateTime.UtcNow >= _delayEnd && !Ai.Owner.IsGlobalCooldowned;
            }
        }

        // TODO: Absolute return dist
        protected bool ShouldReturn =>
            MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.IdlePosition.Local.Position, true) >
            Ai.Owner.Template.ReturnDistance;

        public bool UpdateTarget()
        {
            //We might want to optimize this somehow..
            var aggroList = Ai.Owner.AggroTable.Values;
            var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

            foreach (var abuser in abusers)
            {
                if (Ai.AlreadyTargetted)
                    return true;

                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead && !Ai.AlreadyTargetted)
                {
                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    Ai.AlreadyTargetted = true;
                    return true;
                }
                else
                {
                    Ai.Owner.ClearAggroOfUnit(abuser);
                }
            }
            Ai.Owner.SetTarget(null);
            Ai.AlreadyTargetted = false;
            return false;
        }
    }
}
