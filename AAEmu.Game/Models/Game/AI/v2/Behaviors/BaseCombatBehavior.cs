using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using static AAEmu.Game.Models.Game.Skills.SkillControllers.SkillController;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public abstract class BaseCombatBehavior : Behavior
    {
        protected DateTime _delayEnd;
        protected float _nextTimeToDelay;
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
                Ai.Owner.MoveTowards(target.Position, speed);
            else 
                Ai.Owner.StopMovement();
        }

        protected bool CanStrafe 
        {
            get
            {
                return DateTime.Now > _delayEnd || _strafeDuringDelay;
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
                return DateTime.Now >= _delayEnd && !Ai.Owner.IsGlobalCooldowned;
            }
        }

        // TODO: Absolute return dist
        protected bool ShouldReturn => MathUtil.CalculateDistance(Ai.Owner.Position, Ai.IdlePosition) > Ai.Owner.Template.ReturnDistance;

        public bool UpdateTarget()
        {
            //We might want to optimize this somehow..
            var aggroList = Ai.Owner.AggroTable.Values;
            var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

            foreach(var abuser in abusers)
            {
                if(Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    return true;
                }
                else
                {
                    Ai.Owner.ClearAggroOfUnit(abuser);
                }
            }
            Ai.Owner.SetTarget(null);
            return false;
        }
        
        // UseSkill (delay)
        public void UseSkill(Skill skill, BaseUnit target, float delay = 0)
        {
            _nextTimeToDelay = delay;
            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = Ai.Owner.ObjId;

            SkillCastTarget skillCastTarget;
            switch (skill.Template.TargetType)
            {
                case SkillTargetType.Pos:
                    var pos = Ai.Owner.Position;
                    skillCastTarget = new SkillCastPositionTarget()
                    {
                        ObjId = Ai.Owner.ObjId,
                        PosX = pos.X,
                        PosY = pos.Y,
                        PosZ = pos.Z,
                        PosRot = (float)MathUtil.ConvertDirectionToDegree(pos.RotationZ) //Is this rotation right?
                    };
                    break;
                default:
                    skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                    skillCastTarget.ObjId = target.ObjId;
                    break;
            }

            var skillObject = SkillObject.GetByType(SkillObjectType.None);

            skill.Callback = OnSkillEnded;
            var result = skill.Use(Ai.Owner, skillCaster, skillCastTarget, skillObject);
            if (result == SkillResult.Success)
                Ai.Owner.LookTowards(target.Position);
        }

        public virtual void OnSkillEnded()
        {
            try
            {
                _delayEnd = DateTime.Now.AddSeconds(_nextTimeToDelay);
            }
            catch
            {
                
            }
        }
        
        // Check if can pick a new skill (delay, already casting)
    }
}
