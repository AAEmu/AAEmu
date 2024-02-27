using System;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params.Flytrap;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Flytrap;

public class FlytrapAttackBehavior : Behavior
{
    private FlytrapAiParams _aiParams;
    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Param = Ai.Owner.Template.AiParams;
    }

    public override void Tick(TimeSpan delta)
    {
        if (Ai.Param is not FlytrapAiParams aiParams)
            return;

        _aiParams = aiParams;

        if (!UpdateTarget())
        {
            Ai.GoToReturn();
            return;
        }

        if (Ai.Owner.CurrentTarget == null)
            return;

        if (Ai.Owner.Gimmick?.CurrentTarget != null)
            MoveInRange(Ai.Owner.Gimmick.CurrentTarget, delta);

        Ai.Owner.IsInBattle = true;
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);

        Update();
    }

    public override void Exit()
    {
    }

    #region Gimmick
    private void MoveInRange(BaseUnit target, TimeSpan delta)
    {
        if (Ai?.Owner?.Gimmick == null)
            return;

        var gimmick = Ai.Owner.Gimmick;
        var gimmickPosition = Ai.Owner.Gimmick.Transform.World.Position;
        if (gimmick.Target == Vector3.Zero)
        {
            gimmick.Target = target.Transform.World.Position;
        }
        var finalPoint = gimmick.Target;

        var range = 0.1f;
        var moveDistance = gimmick.BaseMoveSpeed * (delta.Milliseconds / 1000.0f) + 1f;
        var moveDistanceZ = gimmick.Template.Gravity * (delta.Milliseconds / 1000.0f);
        var distanceToTarget = MathUtil.CalculateDistance(gimmickPosition, gimmick.Target, true);

        if (AppConfiguration.Instance.World.GeoDataMode && Ai.Owner.Transform.WorldId > 0)
        {
            // we will find the path to the abuser only if the target coordinates have changed
            if (Ai.PathNode?.findPath?.Count == 0 && target != null && Ai.PathNode?.pos2 != null)
            {
                //if (!Ai.PathNode.pos2.Equals(new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z)))
                {
                    // let's find the path to the abuser
                    Ai.Owner.FindPath((Unit)target);
                    // remember the new target coordinates
                    Ai.PathNode.pos2 = new Point(target.Transform.World.Position.X, target.Transform.World.Position.Y, target.Transform.World.Position.Z);
                    gimmick.Target = target.Transform.World.Position;
                }
            }
            // moving along the route points
            if (Ai.PathNode?.findPath?.Count > 0 && !Ai.PathNode.findPath[0].Equals(Point.Zero))
            {
                // take the point to which we are moving
                var routePoint = new Vector3(Ai.PathNode.Position.X, Ai.PathNode.Position.Y, Ai.PathNode.Position.Z);
                // recalculate the distance, since the path is divided into points
                var distanceToPoint = MathUtil.CalculateDistance(gimmickPosition, routePoint, true);
                if (distanceToPoint > range)
                {
                    gimmick.MoveTowards(routePoint, moveDistance, moveDistanceZ);
                }
                else
                {
                    // take the next point to which we are moving
                    Ai.PathNode.Current++;
                    if (Ai.PathNode.Current >= Ai.PathNode.findPath.Count)
                    {
                        gimmick.StopMovement();
                        Ai.PathNode.findPath = [];
                        return;
                    }

                    Ai.PathNode.Position = Ai.PathNode.findPath[(int)Ai.PathNode.Current];
                }
            }
            else // we move straight to the final point
            {
                if (distanceToTarget > range)
                    gimmick.MoveTowards(finalPoint, moveDistance, moveDistanceZ);
                else
                    gimmick.StopMovement();
            }
        }
        else // we move straight to the final point
        {
            if (distanceToTarget > range)
                gimmick.MoveTowards(finalPoint, moveDistance, moveDistanceZ);
            else
                gimmick.StopMovement();
        }
    }

    private bool UpdateTarget()
    {
        // We might want to optimize this somehow...
        var aggroList = Ai.Owner.AggroTable.Values;
        var abusers = aggroList.OrderByDescending(o => o.TotalAggro).Select(o => o.Owner).ToList();

        foreach (var abuser in abusers)
        {
            Ai.Owner.LookTowards(abuser.Transform.World.Position);
            if (Ai.AlreadyTargetted)
                return true;

            if (AppConfiguration.Instance.World.GeoDataMode && Ai.Owner.Transform.WorldId > 0)
            {
                // включена геодата и не основной мир
                // geodata enabled and not the main world
                if (Ai.Owner.UnitIsVisible(abuser) && !abuser.IsDead)
                {
                    Ai.Owner.CurrentAggroTarget = abuser.ObjId;
                    Ai.Owner.SetTarget(abuser);
                    UpdateAggroHelp(abuser);
                    // TODO найдем путь к abuser
                    Ai.Owner.FindPath(abuser);
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
    #endregion

    public void Update()
    {
        var abuser = (Unit)Ai.Owner.CurrentTarget;
        var abuserPos = Ai.Owner.CurrentTarget.Transform.World.Position;
        var currentPos = Ai.Owner.Transform.World.Position;
        var idlePos = Ai.IdlePosition.World.Position;
        // Check out of idle pos
        if (Ai.Param.AlwaysTeleportOnReturn && MathUtil.DistanceSqVectors(currentPos, idlePos) > 3 * 3)
        {
            // NpcTeleportTo(entity.AI.idlePos);
            Ai.Owner.ClearAggroOfUnit(abuser);
            Ai.GoToReturn();
            return;
        }

        // Check that some target was gone out from attack end distance
        if (MathUtil.DistanceSqVectors(abuserPos, idlePos) > _aiParams.AttackEndDistance * _aiParams.AttackEndDistance)
        {
            // entity.unit:NpcRemoveAggroOutOfRange(entity.AI.param.attackEndDistance);
            Ai.Owner.ClearAggroOfUnit(abuser);
            Ai.GoToReturn();
        }
    }
}
