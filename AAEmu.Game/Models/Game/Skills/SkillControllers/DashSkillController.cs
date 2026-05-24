using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class DashSkillController : SkillController
{
    public int Duration { get; set; }
    public int DistanceOffset { get; set; }

    private readonly float _calculatedSpeed;
    private readonly Vector3 _endPosition;

    public enum DashDirection
    {
        Both = 0,
        ForwardOnly = 1,
        BackwardOnly = 2
    }

    public DashDirection Direction { get; set; }

    public DashSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;

        // template.Value[0] (Angle) and template.Value[1] (Speed) are intentionally
        // ignored: the dash direction is fixed (caster -> target, optionally flipped
        // 180° for BackwardOnly) and the speed is derived from DistanceOffset /
        // Duration so the dash always lands in exactly the configured time.
        Duration = template.Value[2];
        DistanceOffset = template.Value[3];
        Direction = (DashDirection)template.Value[6];

        var angleDeg = (float)MathUtil.CalculateAngleFrom(owner.Transform.World.Position, target.Transform.World.Position);
        var distMeters = DistanceOffset / 1000f;

        if (Direction == DashDirection.BackwardOnly)
            angleDeg += 180f;

        var angleRad = (float)(angleDeg * Math.PI / 180.0);

        (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(
            distMeters,
            owner.Transform.World.Position.X,
            owner.Transform.World.Position.Y,
            angleRad);
        _endPosition.Z = owner.Transform.World.Position.Z;

        var totalDist = MathUtil.CalculateDistance(owner.Transform.World.Position, _endPosition, true);
        var durationSec = Duration > 0 ? Duration / 1000f : 0.5f;
        _calculatedSpeed = totalDist / durationSec;
    }

    public void Tick(TimeSpan delta)
    {
        if (Owner == null)
        {
            End();
            return;
        }
        if (Owner.IsDead)
        {
            End();
            return;
        }
        if (SourceBuffId == 0 && Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep))
        {
            End();
            return;
        }

        MoveTowards(_calculatedSpeed * (float)(delta.TotalMilliseconds / 1000f));
    }

    public override void Execute()
    {
        base.Execute();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
    }

    public override void End(bool force = false)
    {
        base.End(force);
        TickManager.Instance.OnTick.UnSubscribe(Tick);
    }

    public void MoveTowards(float distance, byte actorFlags = 4)
    {
        if (SourceBuffId == 0)
        {
            distance *= Owner.MoveSpeedMul;
            if (distance < 0.01f)
            {
                End();
                return;
            }

            if (Owner.Buffs.HasEffectsMatchingCondition(e =>
                    e.Template.Stun || e.Template.Sleep || e.Template.Root
                    || e.Template.Knockdown || e.Template.Fastened)
                || Owner.IsDead)
            {
                return;
            }

            if (Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
                Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
            {
                return;
            }
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, _endPosition, true);
        if (targetDist <= 0.5f)
        {
            End();
            return;
        }

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        var travelDist = Math.Min(targetDist, distance);

        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(
            travelDist, targetDist, Owner.Transform.Local.Position, _endPosition);
        Owner.Transform.Local.SetPosition(newX, newY, newZ);

        var updZ = Owner.ParentWorld.Template.GeoData.GetHeight(Owner.Transform.World.Position);
        if (Math.Abs(newZ - updZ) < 1f)
            Owner.Transform.Local.SetHeight(updZ);

        var angle = MathUtil.CalculateAngleFrom(Owner.Transform.Local.Position, _endPosition);
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
        Owner.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = Owner.Transform.Local.Position.X;
        moveType.Y = Owner.Transform.Local.Position.Y;
        moveType.Z = Owner.Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = actorFlags;
        moveType.Flags = MoveTypeFlags.Moving;

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
