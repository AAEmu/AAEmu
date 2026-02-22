using System.Numerics;

using AAEmu.Game.Core.Managers.World;
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
            Ai.Owner.BroadcastPacket(new SCUnitPointsPacket(Ai.Owner.ObjId, Ai.Owner.Hp, Ai.Owner.Mp), true);
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
            // Same logic as combat: straight line by default, A* only when wall blocks path
            var wallBlocked = Ai.Owner.ParentWorld?.Template?.GeoData?
                .LinePassesThroughForbiddenArea(currentPos, Ai.IdlePosition) ?? false;

            if (!wallBlocked)
            {
                // Clear path — straight line to idle position
                Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                if (Ai.PathNode?.FoundPath?.Count > 0)
                    Ai.PathNode.FoundPath = [];
            }
            else
            {
                // Wall detected on full line — check if the IMMEDIATE path is also blocked.
                // On ramps, the full line may clip distant forbidden areas, but the next
                // few meters are clear. In that case, prefer straight-line movement.
                var dirToIdle = Vector3.Normalize(Ai.IdlePosition - currentPos);
                var checkDist = MathF.Min(5f, (float)distanceToIdle);
                var shortTarget = currentPos + dirToIdle * checkDist;
                var immediateBlocked = Ai.Owner.ParentWorld?.Template?.GeoData?
                    .LinePassesThroughForbiddenArea(currentPos, shortTarget) ?? false;

                if (!immediateBlocked)
                {
                    // Immediate path is clear — wall is far ahead (ramp / distant obstacle)
                    Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                    if (Ai.PathNode?.FoundPath?.Count > 0)
                        Ai.PathNode.FoundPath = [];
                }
                else if (Ai.PathNode != null)
                {
                    // Wall also blocks immediate path — use A* to navigate around
                    if (Ai.PathNode.FoundPath.Count == 0)
                    {
                        Ai.PathNode.StartPointPos = currentPos;
                        Ai.PathNode.EndPointPos = Ai.IdlePosition;
                        Ai.PathNode.ZoneKey = Ai.Owner.Transform.ZoneId;
                        var resList = Ai.PathNode.FindPath(Ai.Owner.ParentWorld, currentPos, Ai.IdlePosition);
                        resList.Add(Ai.IdlePosition);
                        Ai.PathNode.FoundPath = Ai.Owner.ParentWorld.Template.GeoData.ReducePath(resList, 10);
                    }

                    if (Ai.PathNode.FoundPath.Count > 0)
                    {
                        // Re-check line of sight — if clear, drop A* and go straight
                        var nowClear = !(Ai.Owner.ParentWorld?.Template?.GeoData?
                            .LinePassesThroughForbiddenArea(currentPos, Ai.IdlePosition) ?? false);
                        if (nowClear)
                        {
                            Ai.PathNode.FoundPath = [];
                            Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                        }
                        else
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
                    }
                    else
                    {
                        Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                    }
                }
                else
                {
                    // No PathNode — direct movement only
                    Ai.Owner.MoveTowards(Ai.IdlePosition, (float)moveSpeed, moveFlags);
                }
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
    /// Corrects the Z coordinate of the NPC's current and idle positions
    /// to match the terrain height, preventing NPCs from floating after return.
    /// </summary>
    private void CorrectIdlePositionZ()
    {
        if (Ai.Owner.CanFly)
            return;

        var zoneId = Ai.Owner.Transform.ZoneId;

        // Correct current position if floating
        var currentPos = Ai.Owner.Transform.World.Position;
        var terrainZ = WorldManager.Instance.GetHeight(zoneId, currentPos.X, currentPos.Y, currentPos.Z);
        if (terrainZ > 0f && currentPos.Z > terrainZ + 0.5f)
        {
            Ai.Owner.Transform.Local.SetHeight(terrainZ);
        }

        // Correct stored IdlePosition if floating
        var idleTerrainZ = WorldManager.Instance.GetHeight(zoneId, Ai.IdlePosition.X, Ai.IdlePosition.Y, Ai.IdlePosition.Z);
        if (idleTerrainZ > 0f && Ai.IdlePosition.Z > idleTerrainZ + 0.5f)
        {
            Ai.IdlePosition = Ai.IdlePosition with { Z = idleTerrainZ };
        }
    }

    public override void Exit()
    {
        // TODO: Ai.Owner.EnableAggro();
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, true), false);
        Ai.Owner.Buffs.RemoveBuff((uint)BuffConstants.NpcReturn);
        _enter = false;
    }
}
