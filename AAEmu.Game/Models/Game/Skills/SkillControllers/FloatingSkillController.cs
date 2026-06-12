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

public class FloatingSkillController : SkillController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Vector3 _endPosition;
    private readonly float _speed;
    private readonly float _pullDistance;
    private readonly float _stopDistance;
    private readonly bool _isLiftMode;
    private readonly float _liftHeight;
    private readonly float _liftSpeed;
    private readonly float _liftDuration;
    private bool _isFalling;
    private float _fallSpeed;
    private const float Gravity = 9.81f;
    private const float MaxFallSpeed = 20f;
    private float _startZ;
    private DateTime _liftStartTime;

    public FloatingSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target,
        float psychokinesisSpeed = 0f, float liftHeight = 0f, float liftSpeed = 0f, float liftDuration = 0f)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;

        _liftHeight = liftHeight;
        _liftSpeed = liftSpeed > 0f ? liftSpeed : 3f;
        _liftDuration = liftDuration;
        _isLiftMode = _liftHeight > 0f;

        if (_isLiftMode)
        {
            _speed = _liftSpeed;
            _pullDistance = 0f;
            _stopDistance = 0f;
            _endPosition = owner.Transform.World.Position;

            Logger.Debug("FloatingSC [LIFT]: owner={0} liftHeight={1:F1} liftSpeed={2:F1} liftDuration={3:F1}",
                owner.ObjId, _liftHeight, _liftSpeed, _liftDuration);
        }
        else
        {
            _speed = psychokinesisSpeed > 0f ? psychokinesisSpeed
                : (template != null && template.Value[0] > 0 ? template.Value[0] / 1000f : 5f);

            var ownerPos = owner.Transform.World.Position;
            var targetPos = target.Transform.World.Position;
            var dist = MathUtil.CalculateDistance(ownerPos, targetPos, true);

            _stopDistance = 1.5f;
            _pullDistance = Math.Max(0f, dist - _stopDistance);

            if (dist > _stopDistance)
            {
                var direction = Vector3.Normalize(targetPos - ownerPos);
                direction.Z = 0;
                if (direction.LengthSquared() < 0.01f)
                    direction = new Vector3(1, 0, 0);
                direction = Vector3.Normalize(direction);
                _endPosition = ownerPos + direction * _pullDistance;
                _endPosition.Z = ownerPos.Z;
            }
            else
            {
                _endPosition = ownerPos;
            }

            Logger.Debug("FloatingSC [PULL]: owner={0} target={1} dist={2:F1} pullDist={3:F1} speed={4:F1} endPos=({5:F1},{6:F1},{7:F1})",
                owner.ObjId, target.ObjId, dist, _pullDistance, _speed,
                _endPosition.X, _endPosition.Y, _endPosition.Z);
        }
    }

    public override void Execute()
    {
        base.Execute();

        if (_isLiftMode)
        {
            _startZ = Owner.Transform.Local.Position.Z;
            _liftStartTime = DateTime.UtcNow;
            Logger.Debug("FloatingSC.Execute [LIFT]: owner={0} startZ={1:F1} targetZ={2:F1}",
                Owner.ObjId, _startZ, _startZ + _liftHeight);
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
        }
        else
        {
            if (Owner == null || _pullDistance <= 0.5f)
            {
                Logger.Debug("FloatingSC.Execute [PULL]: owner={0} already close or null, skipping", Owner?.ObjId);
                End();
                return;
            }

            Logger.Debug("FloatingSC.Execute [PULL]: owner={0} pulling toward target={1}, dist={2:F1}m, speed={3:F1}",
                Owner.ObjId, Target?.ObjId, _pullDistance, _speed);
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
        }
    }

    public override void End(bool force = false)
    {
        // Lift→Fall transition: switch to fall phase instead of teleporting to ground.
        // Skipped when force=true (the caller is about to replace this SC with a new
        // one — keeping the fall phase alive would let the old controller keep ticking
        // alongside the new one and produce conflicting movement broadcasts).
        if (!force && _isLiftMode && !_isFalling && Owner != null && !Owner.IsDead)
        {
            var groundZ = GetGroundHeight();
            if (groundZ > 0 && Owner.Transform.Local.Position.Z > groundZ + 0.5f)
            {
                Logger.Debug("FloatingSC.End [LIFT→FALL]: owner={0} starting fall from Z={1:F1} to ground={2:F1}",
                    Owner.ObjId, Owner.Transform.Local.Position.Z, groundZ);
                _isFalling = true;
                _fallSpeed = 0f;
                State = SCState.Running;
                return;
            }
        }

        base.End(force);
        TickManager.Instance.OnTick.UnSubscribe(Tick);
    }

    private void Tick(TimeSpan delta)
    {
        if (Owner == null)
        {
            FinalEnd();
            return;
        }

        if (Owner.IsDead)
        {
            Logger.Debug("FloatingSC.Tick: owner={0} dead, ending", Owner.ObjId);
            FinalEnd();
            return;
        }

        if (_isFalling)
        {
            FallTick(delta);
            return;
        }

        if (SourceBuffId == 0 && Owner.Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun || e.Template.Sleep || e.Template.Knockdown))
        {
            Logger.Debug("FloatingSC.Tick: owner={0} CC'd (no source buff), ending", Owner.ObjId);
            End();
            return;
        }

        if (_isLiftMode)
            LiftTick(delta);
        else
            PullTick(delta);
    }

    private void FinalEnd()
    {
        base.End();
        TickManager.Instance.OnTick.UnSubscribe(Tick);
    }

    private void LiftTick(TimeSpan delta)
    {
        var currentPos = Owner.Transform.Local.Position;
        var currentHeight = currentPos.Z - _startZ;

        // Duration check FIRST so the timed descent fires regardless of whether
        // the NPC is still ascending or already at hold height. If this sat
        // after the height-hold early-return below, the descent timer would
        // only ever trigger during the brief ascent window and never during
        // the hold itself — the buff dispel would then be the only path that
        // ever ended the lift.
        if (_liftDuration > 0f)
        {
            var elapsed = (DateTime.UtcNow - _liftStartTime).TotalSeconds;
            if (elapsed >= _liftDuration)
            {
                Logger.Debug("FloatingSC [LIFT]: owner={0} lift duration expired after {1:F1}s — descending", Owner.ObjId, elapsed);
                End();
                return;
            }
        }

        if (currentHeight >= _liftHeight)
        {
            Logger.Debug("FloatingSC [LIFT]: owner={0} reached lift height {1:F1}, holding", Owner.ObjId, _liftHeight);
            return;
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var liftDist = _liftSpeed * (float)(delta.TotalMilliseconds / 1000f);
        var remainingHeight = _liftHeight - currentHeight;
        liftDist = Math.Min(liftDist, remainingHeight);

        var newZ = currentPos.Z + liftDist;
        Owner.Transform.Local.SetPosition(currentPos.X, currentPos.Y, newZ);

        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = currentPos.X;
        moveType.Y = currentPos.Y;
        moveType.Z = newZ;
        moveType.VelX = 0;
        moveType.VelY = 0;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = 5; // Airborne/Floating
        moveType.Flags = MoveTypeFlags.Moving | (Owner is Npc combatNpc && combatNpc.IsInBattle ? MoveTypeFlags.InCombat : 0);
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = 127; // Upward
        moveType.Stance = 0;
        moveType.Alertness = MoveTypeAlertness.Combat;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        Owner.CheckMovedPosition(oldPosition);
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
    }

    private void FallTick(TimeSpan delta)
    {
        var dt = (float)(delta.TotalMilliseconds / 1000f);
        var currentPos = Owner.Transform.Local.Position;
        var groundZ = GetGroundHeight();

        if (groundZ <= 0) groundZ = _startZ;

        if (currentPos.Z <= groundZ + 0.1f)
        {
            Logger.Debug("FloatingSC [FALL]: owner={0} reached ground Z={1:F1}", Owner.ObjId, groundZ);
            Owner.Transform.Local.SetHeight(groundZ);

            // End SC state BEFORE StopMovement so the SC guard in Npc.MoveTowards passes.
            FinalEnd();

            if (Owner is Npc npc)
                npc.StopMovement();

            return;
        }

        _fallSpeed = Math.Min(_fallSpeed + Gravity * dt, MaxFallSpeed);
        var fallDist = _fallSpeed * dt;
        var newZ = Math.Max(currentPos.Z - fallDist, groundZ);

        var oldPosition = Owner.Transform.Local.ClonePosition();
        Owner.Transform.Local.SetPosition(currentPos.X, currentPos.Y, newZ);

        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = currentPos.X;
        moveType.Y = currentPos.Y;
        moveType.Z = newZ;
        moveType.VelX = 0;
        moveType.VelY = 0;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = 5;
        moveType.Flags = MoveTypeFlags.Moving | (Owner is Npc combatNpc && combatNpc.IsInBattle ? MoveTypeFlags.InCombat : 0);
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = -127;
        moveType.Stance = 0;
        moveType.Alertness = MoveTypeAlertness.Combat;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        Owner.CheckMovedPosition(oldPosition);
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
    }

    private float GetGroundHeight()
    {
        var geoZ = Owner.ParentWorld.Template.GeoData.GetHeight(Owner.Transform.World.Position);
        return geoZ > 0 ? geoZ : _startZ;
    }

    private void PullTick(TimeSpan delta)
    {
        var moveDist = _speed * (float)(delta.TotalMilliseconds / 1000f);
        MoveTowardsTarget(moveDist);
    }

    private void MoveTowardsTarget(float distance)
    {
        if (distance < 0.01f)
        {
            End();
            return;
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var currentPos = Owner.Transform.Local.Position;
        var targetDist = MathUtil.CalculateDistance(currentPos, _endPosition, true);

        if (targetDist <= 0.5f)
        {
            Logger.Debug("FloatingSC [PULL]: owner={0} reached pull endpoint (dist={1:F2}), ending", Owner.ObjId, targetDist);
            End();
            return;
        }

        var travelDist = Math.Min(targetDist, distance);

        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(
            travelDist, targetDist, currentPos, _endPosition);
        Owner.Transform.Local.SetPosition(newX, newY, newZ);

        var updZ = Owner.ParentWorld.Template.GeoData.GetHeight(Owner.Transform.World.Position);
        if (Math.Abs(newZ - updZ) < 1f)
            Owner.Transform.Local.SetHeight(updZ);

        var angle = MathUtil.CalculateAngleFrom(Owner.Transform.Local.Position, _endPosition);
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
        moveType.Flags = MoveTypeFlags.Moving | (Owner is Npc combatNpc && combatNpc.IsInBattle ? MoveTypeFlags.InCombat : 0);
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
