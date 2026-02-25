using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class ReturnStateBehavior : BaseCombatBehavior
{
    private DateTime _timeoutTime;
    private DateTime _lastPathRecalcTime;
    private bool _enter;

    public override void Enter()
    {
        // TODO : Autodisable

        if (!Ai.Owner.AggroTable.IsEmpty)
            Ai.Owner.ClearAllAggro();

        Ai.Owner.SetTarget(null);
        // TODO: Ai.Owner.DisableAggro();

        Ai.Owner.IsInBattle = false;
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);

        // Ai.AiPathPointsRemaining.Clear(); // Remove whatever path we're on
        // Ai.Owner.Simulation.TargetPosition = Vector3.Zero; // And reset expected target

        //var needRestorationOnReturn = true; // TODO: Use params & alertness values
        //if (needRestorationOnReturn)
        // StartSkill RETURN SKILL TYPE
        Ai.Owner.Buffs.AddBuff((uint)BuffConstants.NpcReturn, Ai.Owner);
        if (Ai.Param == null || Ai.Param.RestorationOnReturn)
        {
            Ai.Owner.PostUpdateCurrentHp(Ai.Owner, Ai.Owner.Hp, Ai.Owner.MaxHp, KillReason.Unknown);
            Ai.Owner.Hp = Ai.Owner.MaxHp;
            Ai.Owner.Mp = Ai.Owner.MaxMp;
            Ai.Owner.BroadcastPacket(new SCUnitPointsPacket(Ai.Owner), true);
        }

        //var alwaysTeleportOnReturn = false; // TODO: get from params
        //if (alwaysTeleportOnReturn)
        if (Ai.Param is { AlwaysTeleportOnReturn: true })
        {
            OnCompletedReturn();
            return;
        }

        //var goReturnState = true; // TODO: get from params
        //if (!goReturnState)
        if (Ai.Param is { GoReturnState: false })
        {
            OnCompletedReturnNoTeleport();
        }

        if (Ai.PathNode?.FoundPath?.Count > 0)
            Ai.PathNode.FoundPath = [];

        _timeoutTime = DateTime.UtcNow.AddSeconds(20);
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        var moveSpeed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
        var moveFlags = Ai.GetRealMovementFlags(moveSpeed);
        moveSpeed *= delta.Milliseconds / 1000.0;

        var currentPos = Ai.Owner.Transform.World.Position;
        var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, currentPos);

        if (distanceToIdle < 1.0f)
        {
            OnCompletedReturnNoTeleport();
            return;
        }

        if (AppConfiguration.Instance.World.GeoDataMode)
        {
            // NavMesh Raycast checks if path crosses collision walls (brush meshes
            // + forbidden area walls). Falls back to ForbiddenArea polygon check
            // when NavMesh has no poly data near the NPC.
            var navMesh = Ai.Owner.ParentWorld?.NavMesh;
            bool pathClear;
            if (navMesh?.HasData == true && navMesh.TryRaycast(currentPos, Ai.IdlePosition, out var raycastClear))
            {
                pathClear = raycastClear;
            }
            else
            {
                // ForbiddenArea fallback disabled — navmesh is the authoritative source.
                // ForbiddenArea polygons often cover building interiors (including staircases),
                // causing NPCs to unnecessarily trigger full pathfinding or abort returns.
                pathClear = true;

                if (!Ai.Owner.CanFly)
                {
                    var heightDiff = MathF.Abs(Ai.IdlePosition.Z - currentPos.Z);
                    if (heightDiff > 3f && (distanceToIdle < 1f || heightDiff / distanceToIdle > 0.5f))
                        pathClear = false;
                }
            }

            if (pathClear)
            {
                // Clear line of sight — straight line to idle position
                Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                if (Ai.PathNode?.FoundPath?.Count > 0)
                    Ai.PathNode.FoundPath = [];
            }
            else if (Ai.PathNode != null)
            {
                // Blocked — use A* (BAI-first with NavMesh refinement)
                if (Ai.PathNode.FoundPath.Count == 0 && DateTime.UtcNow > _lastPathRecalcTime.AddSeconds(2))
                {
                    Ai.PathNode.StartPointPos = currentPos;
                    Ai.PathNode.EndPointPos = Ai.IdlePosition;
                    Ai.PathNode.ZoneKey = Ai.Owner.Transform.ZoneId;
                    var resList = Ai.PathNode.FindPath(Ai.Owner.ParentWorld, currentPos, Ai.IdlePosition);
                    if (navMesh?.HasData == true)
                        Ai.PathNode.FoundPath = AiGeoDataManager.ReducePathNavMesh(resList, 10, navMesh);
                    else
                        Ai.PathNode.FoundPath = Ai.Owner.ParentWorld.Template.GeoData.ReducePath(resList, 10);
                    _lastPathRecalcTime = DateTime.UtcNow;
                }

                if (Ai.PathNode.FoundPath.Count > 0)
                {
                    var nextPoint = Ai.PathNode.FoundPath.Peek();
                    var distToPoint = MathUtil.CalculateDistance(currentPos, nextPoint, true);
                    if (distToPoint > 1.0f)
                    {
                        Ai.Owner.MoveTowards(nextPoint, (float)moveSpeed, moveFlags);
                    }
                    else
                    {
                        Ai.PathNode.FoundPath.Dequeue();
                    }
                }
                else
                {
                    // Path empty — go straight (best effort)
                    Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                }
            }
            else
            {
                Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
            }
        }
        else
        {
            Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
        }

        if (DateTime.UtcNow > _timeoutTime)
            OnCompletedReturn();
    }

    private void OnCompletedReturn()
    {
        var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, Ai.Owner.Transform.World.Position);
        if (distanceToIdle > 2 * 2)
        {
            Ai.Owner.MoveTowards(Ai.IdlePosition, 1000000.0f);
            Ai.Owner.StopMovement();
        }

        CorrectIdlePositionZ();
        OnCompletedReturnNoTeleport();
    }

    public void OnCompletedReturnNoTeleport()
    {
        CorrectIdlePositionZ();
        Ai.GoToIdle();
    }

    /// <summary>
    /// Restores the NPC's Z to the spawner's designed idle position.
    /// No height query — trust the spawner data / A* pathfinding Z.
    /// </summary>
    private void CorrectIdlePositionZ()
    {
        if (Ai.Owner.CanFly)
            return;

        Ai.Owner.Transform.Local.SetHeight(Ai.IdlePosition.Z);
    }

    public override void Exit()
    {
        // TODO: Ai.Owner.EnableAggro();
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, true), false);
        Ai.Owner.Buffs.RemoveBuff((uint)BuffConstants.NpcReturn);
        _enter = false;
    }
}
