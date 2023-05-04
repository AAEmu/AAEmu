using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using static AAEmu.Game.Models.Game.Skills.SkillControllers.SkillController;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public abstract class BaseCombatBehavior : Behavior
    {
        protected bool _strafeDuringDelay;

        private const int decreaseMoveSpeed = 161;
        private const int shackle = 160;
        private const int snare = 27;

        public void MoveInRange(BaseUnit target, TimeSpan delta)
        {
            if (Ai == null || Ai.Owner == null)
                return;

            if (Ai.Owner.Buffs.HasEffectsMatchingCondition(e =>
                    e.Template.Stun ||
                    e.Template.Sleep ||
                    e.Template.Root ||
                    e.Template.Knockdown ||
                    e.Template.Fastened))
            {
                return;
            }

            if ((Ai.Owner.ActiveSkillController?.State ?? SCState.Ended) == SCState.Running)
                return;

            if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(shackle)) ||
                Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(decreaseMoveSpeed)) ||
                Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(snare)))
            {
                return;
            }

            //Ai.Owner.Template.AttackStartRangeScale * 4, 
            //var range = 2f;// Ai.Owner.Template.AttackStartRangeScale * 6;
            var range = Ai.Owner.Template.AttackStartRangeScale;
            var speed = 5.4f * (delta.Milliseconds / 1000.0f);
            var distanceToTarget = Ai.Owner.GetDistanceTo(target, true);

            // TODO найдем путь к abuser, только если координаты цели изменились
            if (Ai.PathNode?.pos2 != null && Ai.PathNode != null && !Ai.PathNode.pos2.Equals(new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z)))
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();
                // TODO найдем путь к abuser
                Ai.Owner.FindPath((Unit)target);
                stopWatch.Stop();
                _log.Info($"FindPath took {stopWatch.Elapsed} for Ai.Owner.ObjId:{Ai.Owner.ObjId}");
                // запомним новые координаты цели
                Ai.PathNode.pos2 = new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z);
            }

            if (Ai.PathNode?.findPath.Count > 0)
            {
                // TODO взять точку к которой движемся
                var position = new Vector3(Ai.PathNode.Position.X, Ai.PathNode.Position.Y, Ai.PathNode.Position.Z);
                distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, position, true);
                if (distanceToTarget > range)
                {
                    Ai.Owner.MoveTowards(position, speed);
                }
                else
                {
                    // TODO взять следующую точку к которой движемся
                    Ai.PathNode.Current++;
                    if (Ai.PathNode.Current >= Ai.PathNode.findPath.Count)
                    {
                        Ai.Owner.StopMovement();
                        Ai.PathNode.findPath = new List<Point>();
                        return;
                    }
                    Ai.PathNode.Position = Ai.PathNode.findPath[(int)Ai.PathNode.Current];
                }
            }
            else
            {
                // var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Position, target.Position, true);
                if (distanceToTarget > range)
                    Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
                else
                    Ai.Owner.StopMovement();
            }
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
                if (Ai.Owner != null && Ai.Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep || e.Template.Silence))
                    return false;
                return Ai.Owner != null && DateTime.UtcNow >= _delayEnd && !Ai.Owner.IsGlobalCooldowned;
            }
        }

        // TODO: Absolute return dist
        protected bool ShouldReturn
        {
            get
            {
                var returnDistance = 50f;
                if (Ai.Owner.Template.ReturnDistance > 0)
                {
                    returnDistance = Ai.Owner.Template.ReturnDistance;
                }
                var res = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.IdlePosition.Local.Position, true) > returnDistance;
                return res;
            }
        }

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

                    // TODO найдем путь к abuser
                    Ai.Owner.FindPath(abuser);

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

    }
}
