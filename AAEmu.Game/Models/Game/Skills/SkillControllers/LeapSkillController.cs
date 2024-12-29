using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class LeapSkillController : SkillController
{
    public int Angle { get; set; }
    public int Speed { get; set; }
    public int Duration { get; set; }
    public int DistanceOffset { get; set; }

    private float _calculatedSpeed;
    private Vector3 _endPosition;
    public enum LeapDirection
    {
        Both = 0,
        ForwardOnly = 1,
        BackwardOnly = 2
    }
    public LeapDirection Direction { get; set; }

    public LeapSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;

        Angle = template.Value[0];
        Speed = template.Value[1];
        Duration = template.Value[2];
        DistanceOffset = template.Value[3];
        Direction = (LeapDirection)template.Value[6];

        var angle = (float)MathUtil.CalculateAngleFrom(owner.Transform.World.Position, target.Transform.World.Position);
        (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(DistanceOffset / 1000f, target.Transform.World.Position.X, target.Transform.World.Position.Y, angle);
        _endPosition.Z = target.Transform.World.Position.Z;

        var distance = MathUtil.CalculateDistance(owner.Transform.World.Position, _endPosition, true);
        _calculatedSpeed = distance / (Duration / 1000f);
    }

    public void Tick(TimeSpan delta)
    {
        if (Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep) || Owner.IsDead)
        {
            End();
            return;
        };
        MoveTowards(_calculatedSpeed * (float)(delta.TotalMilliseconds / 1000f));
    }

    public override void Execute()
    {
        base.Execute();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
    }

    public override void End()
    {
        base.End();
        TickManager.Instance.OnTick.UnSubscribe(Tick);
    }

    public void MoveTowards(float distance, byte actorFlags = 4)
    {
        distance *= Owner.MoveSpeedMul; // Apply speed modifier
        if (distance < 0.01f)
        {
            //TODO End Skill Controller
            End();
            return;
        }

        if (Owner.Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || Owner.IsDead)
        {
            //Logger.Debug($"{ObjId} @NPC_NAME({TemplateId}); is stuck in place");
            return;
        }

        if (Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return;
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, _endPosition, true);
        if (targetDist <= 1f)
        {
            //TODO End Skill Controller
            End();
            return;
        }

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        var travelDist = Math.Min(targetDist, distance);

        // TODO: Implement proper use for Transform.World.AddDistanceToFront
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Owner.Transform.Local.Position, _endPosition);
        Owner.Transform.Local.SetPosition(newX, newY, newZ);

        // TODO: Implement Transform.World to do proper movement
        // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
        var updZ = WorldManager.Instance.GetHeight(Owner.Transform.ZoneId, newX, newY);
        if (Math.Abs(newZ - updZ) < 1f)
        {
            Owner.Transform.Local.SetHeight(updZ);
        }

        var angle = MathUtil.CalculateAngleFrom(Owner.Transform.Local.Position, _endPosition);
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
        Owner.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = Owner.Transform.Local.Position.X;
        moveType.Y = Owner.Transform.Local.Position.Y;
        moveType.Z = Owner.Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        //moveType.VelZ = (short)velZ;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = actorFlags;     // 5-walk, 4-run, 3-stand still
        moveType.Flags = MoveTypeFlags.Moving; // 4

        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 127;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
        moveType.Alertness = MoveTypeAlertness.Combat; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        Owner.CheckMovedPosition(oldPosition);
        //SetPosition(Position);
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
    }
}
