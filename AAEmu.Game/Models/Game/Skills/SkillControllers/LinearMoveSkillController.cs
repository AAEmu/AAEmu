using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// The movement a controller that walks its owner in a straight line to a fixed end position shares: Leap
/// (to the target, pushed out by its distance offset) and Dash (along the facing). The end position and the
/// speed are decided by the subclass; this holds the per-tick step, the ground snap and the movement packet.
/// </summary>
/// <remarks>
/// The step moves the owner's server-side transform and broadcasts <see cref="SCOneUnitMovementPacket"/> for
/// it, which is what makes a controller authoritative for its owner's position. A client prediction of the
/// same move is corrected by that packet.
/// </remarks>
public abstract class LinearMoveSkillController : SkillController
{
    /// <summary>Where the owner is heading, in local coordinates.</summary>
    protected Vector3 EndPosition { get; set; }

    /// <summary>How fast the owner travels toward <see cref="EndPosition"/>, in metres per second.</summary>
    protected float MoveSpeed { get; set; }

    /// <summary>
    /// Set while the controller is walking its owner. A controller that already ended (the owner died, a
    /// root landed, the end position was reached) must not move anybody again.
    /// </summary>
    protected bool Finished => State == SCState.Ended;

    protected LinearMoveSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;
    }

    public virtual void Tick(TimeSpan delta)
    {
        if (Owner == null || Finished)
            return;

        if (Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep) || Owner.IsDead)
        {
            End();
            return;
        }

        MoveTowards(MoveSpeed * (float)(delta.TotalMilliseconds / 1000f));
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

        // The controller is finished with this owner: the owner must not keep pointing at it, or every
        // reader of ActiveSkillController (Npc, Mate and Route/Simulation's position gate) keeps treating a
        // unit that is standing still as one under a controller.
        if (ReferenceEquals(Owner?.ActiveSkillController, this))
            Owner.ActiveSkillController = null;
    }

    public void MoveTowards(float distance, byte actorFlags = 4)
    {
        if (Owner == null || Finished)
            return;

        distance *= Owner.MoveSpeedMul; // Apply speed modifier
        if (distance < 0.01f)
        {
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
            // Held in place: the controller stays alive so the move resumes if the effect drops off.
            return;
        }

        if (Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return;
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, EndPosition, true);
        if (targetDist <= 1f)
        {
            End();
            return;
        }

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        var travelDist = Math.Min(targetDist, distance);

        // A straight line from where the owner is to the end position, one step per tick.
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(
            travelDist, targetDist, Owner.Transform.Local.Position, EndPosition);
        Owner.Transform.Local.SetPosition(newX, newY, newZ);

        // Ground snap. A unit with no world to ask (a unit under construction, a test) keeps the Z the move
        // produced instead of dereferencing a null world.
        var geoData = Owner.ParentWorld?.Template?.GeoData;
        if (geoData != null)
        {
            var updZ = geoData.GetHeight(Owner.Transform.World.Position);
            if (Math.Abs(newZ - updZ) < 1f)
                Owner.Transform.Local.SetHeight(updZ);
        }

        var angle = MathUtil.CalculateAngleFrom(Owner.Transform.Local.Position, EndPosition);
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
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
    }
}
