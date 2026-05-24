using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class WanderingSkillController : SkillController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Vector3 _currentWaypoint;
    private readonly float _duration;
    private readonly float _changeInterval;
    private DateTime _startTime;
    private DateTime _lastDirectionChange;
    private readonly Random _random = new();

    public WanderingSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;

        var rawDuration = template?.Value[1] ?? 0;
        _duration = rawDuration > 0 ? rawDuration / 1000f : 5f;

        var rawInterval = template?.Value[2] ?? 0;
        _changeInterval = rawInterval > 0 ? rawInterval / 1000f : 1.5f;

        PickNewDirection();

        Logger.Debug("WanderingSC: owner={0} duration={1:F1}s interval={2:F1}s",
            owner.ObjId, _duration, _changeInterval);
    }

    private float GetFearSpeed()
    {
        if (Owner is not Npc npc) return 7f;

        var model = ModelManager.Instance.GetActorModel(npc.Template.ModelId);
        if (model == null) return 7f;

        if (!model.Stances.TryGetValue(npc.CurrentGameStance, out var stance))
            return 7f;

        var speed = stance.AiMoveSpeedSprint > 0.1f
            ? Math.Min(stance.AiMoveSpeedSprint, stance.MaxSpeed)
            : Math.Min(stance.AiMoveSpeedRun, stance.MaxSpeed);
        return speed > 0.1f ? speed : 7f;
    }

    public override void Execute()
    {
        base.Execute();
        _startTime = DateTime.UtcNow;
        _lastDirectionChange = _startTime;

        // Clear visual target on client (stop face-target overlay) but keep server-side
        // CurrentTarget/CurrentAggroTarget so ShouldReturn doesn't fire and combat resumes after fear.
        if (Owner is Npc fearNpc)
        {
            fearNpc.BroadcastPacket(
                new SCTargetChangedPacket(fearNpc.ObjId, 0), true);
        }

        Logger.Debug("WanderingSC.Execute: owner={0} running in fear for {1:F1}s speed={2:F1}",
            Owner?.ObjId, _duration, GetFearSpeed());
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
    }

    public override void End(bool force = false)
    {
        base.End(force);
        TickManager.Instance.OnTick.UnSubscribe(Tick);

        if (Owner is Npc endNpc && endNpc.CurrentTarget != null)
        {
            endNpc.BroadcastPacket(
                new SCTargetChangedPacket(endNpc.ObjId, endNpc.CurrentTarget.ObjId), true);
        }

        if (Owner is Npc npc)
            npc.StopMovement();
    }

    private void Tick(TimeSpan delta)
    {
        if (Owner == null)
        {
            End();
            return;
        }

        if (Owner.IsDead)
        {
            Logger.Debug("WanderingSC.Tick: owner={0} dead, ending", Owner.ObjId);
            End();
            return;
        }

        var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        if (elapsed >= _duration)
        {
            Logger.Debug("WanderingSC.Tick: owner={0} fear expired after {1:F1}s", Owner.ObjId, elapsed);
            End();
            return;
        }

        if ((DateTime.UtcNow - _lastDirectionChange).TotalSeconds >= _changeInterval)
        {
            PickNewDirection();
            _lastDirectionChange = DateTime.UtcNow;
        }

        var speed = GetFearSpeed();
        var moveDist = speed * (float)(delta.TotalMilliseconds / 1000f);
        MoveTowardsWaypoint(moveDist);
    }

    private void PickNewDirection()
    {
        if (Owner == null) return;

        var currentPos = Owner.Transform.Local.Position;

        var angle = _random.NextDouble() * 2 * Math.PI;
        var dist = 5f + (float)(_random.NextDouble() * 5f);

        _currentWaypoint = new Vector3(
            currentPos.X + (float)(Math.Cos(angle) * dist),
            currentPos.Y + (float)(Math.Sin(angle) * dist),
            currentPos.Z);

        Logger.Debug("WanderingSC: owner={0} new waypoint ({1:F1},{2:F1},{3:F1})",
            Owner.ObjId, _currentWaypoint.X, _currentWaypoint.Y, _currentWaypoint.Z);
    }

    private void MoveTowardsWaypoint(float distance)
    {
        if (distance < 0.01f) return;

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var currentPos = Owner.Transform.Local.Position;
        var distToWaypoint = MathUtil.CalculateDistance(currentPos, _currentWaypoint, true);

        if (distToWaypoint <= 1f)
        {
            PickNewDirection();
            _lastDirectionChange = DateTime.UtcNow;
            return;
        }

        var travelDist = Math.Min(distToWaypoint, distance);

        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(
            travelDist, distToWaypoint, currentPos, _currentWaypoint);
        Owner.Transform.Local.SetPosition(newX, newY, newZ);

        var updZ = Owner.ParentWorld.Template.GeoData.GetHeight(Owner.Transform.World.Position);
        if (updZ > 0 && Math.Abs(newZ - updZ) < 3f)
            Owner.Transform.Local.SetHeight(updZ);

        var angle = MathUtil.CalculateAngleFrom(Owner.Transform.Local.Position, _currentWaypoint);
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
        Owner.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = Owner.Transform.Local.Position.X;
        moveType.Y = Owner.Transform.Local.Position.Y;
        moveType.Z = Owner.Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = 4;
        moveType.Flags = MoveTypeFlags.Moving | MoveTypeFlags.InCombat;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 127;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = 0;
        moveType.Alertness = MoveTypeAlertness.Combat;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        Owner.CheckMovedPosition(oldPosition);
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
    }
}
