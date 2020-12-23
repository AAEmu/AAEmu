using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public abstract class BaseCombatBehavior : Behavior
    {
        protected DateTime _delayEnd;
        protected float _nextTimeToDelay;
        protected bool _strafeDuringDelay;
        
        public void MoveInRange(BaseUnit target, float range, float speed)
        {
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
                if (Ai.Owner.SkillTask != null || Ai.Owner.ActivePlotState != null)
                    return false;
                return DateTime.UtcNow >= _delayEnd && !Ai.Owner.IsGlobalCooldowned;
            }
        }

        public void UpdateAggro()
        {

        }

        public void UpdateTarget()
        {
            var topAbuser = Ai.Owner.AggroTable.GetTopTotalAggroAbuserObjId();
            if ((Ai.Owner.CurrentTarget?.ObjId ?? 0) != topAbuser)
            {
                Ai.Owner.CurrentAggroTarget = topAbuser;
                var unit = WorldManager.Instance.GetUnit(topAbuser);
                Ai.Owner.SetTarget(unit);
            }
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
                _delayEnd = DateTime.UtcNow.AddSeconds(_nextTimeToDelay);
            }
            catch (Exception e){}
        }
        
        // Check if can pick a new skill (delay, already casting)
    }
}
