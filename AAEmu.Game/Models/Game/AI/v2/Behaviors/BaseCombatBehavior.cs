using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors;

public abstract class BaseCombatBehavior : Behavior
{
    protected bool _strafeDuringDelay;

    public void MoveInRange(BaseUnit target, TimeSpan delta)
    {
        if (Ai?.Owner == null)
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

        if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return;
        }

        if ((Ai.Owner.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return;

        //Ai.Owner.Template.AttackStartRangeScale * 4,
        //var range = 2f;// Ai.Owner.Template.AttackStartRangeScale * 6;
        var range = Ai.Owner.Template.AttackStartRangeScale;
        if (Ai.Owner.Template.UseRangeMod)
        {
            range *= _maxWeaponRange;
        }
        var speed = Ai.Owner.BaseMoveSpeed * (delta.Milliseconds / 1000.0f);
        var distanceToTarget = Ai.Owner.GetDistanceTo(target, true);

        if (AppConfiguration.Instance.World.GeoDataMode && Ai.Owner.Transform.WorldId > 0)
        {
            // TODO найдем путь к abuser, только если координаты цели изменились
            if (target != null && Ai.PathNode?.pos2 != null && Ai.PathNode != null)
            {
                if (!Ai.PathNode.pos2.Equals(new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z)))
                {
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();
                    // TODO найдем путь к abuser
                    Ai.Owner.FindPath((Unit)target);
                    stopWatch.Stop();
                    // Toss warning if it took a long time
                    if (stopWatch.Elapsed.Ticks >= TimeSpan.TicksPerMillisecond)
                        Logger.Warn($"FindPath took {stopWatch.Elapsed} for Ai.Owner.ObjId:{Ai.Owner.ObjId}, Owner.TemplateId {Ai.Owner.TemplateId}");
                    // запомним новые координаты цели
                    Ai.PathNode.pos2 = new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z);
                }
            }

            if (target != null && Ai.PathNode != null)
            {
                if (Ai.PathNode.findPath.Count > 0 && !Ai.PathNode.findPath[0].Equals(Point.Zero))
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
                    if (distanceToTarget > range)
                        Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
                    else
                        Ai.Owner.StopMovement();
                }
            }
            else
            {
                if (distanceToTarget > range && target != null)
                    Ai.Owner.MoveTowards(target.Transform.World.Position, speed);
                else
                    Ai.Owner.StopMovement();
            }
        }
        else
        {
            if (distanceToTarget > range && target != null)
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
            if ((Ai.Owner?.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
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
            var absoluteReturnDistance = 200f;

            if (Ai.Owner.Template.ReturnDistance > 0)
            {
                returnDistance = Ai.Owner.Template.ReturnDistance;
            }
            if (Ai.Owner.Template.AbsoluteReturnDistance > 0)
            {
                absoluteReturnDistance = Ai.Owner.Template.AbsoluteReturnDistance;
            }

            if (Ai.Owner.CurrentTarget == null)
                return true; // нет цели, возвращаемся

            var distanceToTarget = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.Owner.CurrentTarget.Transform.World.Position, true);
            var distanceToIdlePosition = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, Ai.IdlePosition.Local.Position, true);

            var res = distanceToTarget > returnDistance || distanceToIdlePosition > returnDistance;
            if (res)
                res = distanceToIdlePosition <= absoluteReturnDistance; // если больше, то нужен телепорт на место спавна
            return res;
        }
    }

    public bool UpdateTarget()
    {
        // We might want to optimize this somehow.
        var aggroList = Ai.Owner.AggroTable.Values;
        var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

        foreach (var abuser in abusers)
        {
            //Ai.Owner.LookTowards(abuser.Transform.World.Position); // Prevents archers from escaping (they spin around all the time)

            if (AppConfiguration.Instance.World.GeoDataMode && Ai.Owner.Transform.WorldId > 0)
            {
                // включена геодата и не основной мир
                // geodata enabled and not the main world
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    if (Ai.Owner.CurrentAggroTarget != abuser.ObjId && !Ai.AlreadyTargetted)
                    {
                        // TODO найдем путь к abuser
                        Ai.Owner.FindPath(abuser);
                    }
                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);

                    return true;
                }
            }
            else
            {
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    return true;
                }
            }
            Ai.Owner.ClearAggroOfUnit(abuser);
        }

        Ai.Owner.SetTarget(null);
        return false;
    }
}
