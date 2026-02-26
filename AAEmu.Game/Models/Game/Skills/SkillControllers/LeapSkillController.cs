using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
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

    private readonly float _calculatedSpeed;
    private readonly Vector3 _endPosition;

    // ── Aquatic submerge phase ──
    // When an aquatic NPC starts a leap, it first sinks deeper underwater
    // (like the original Kraken behavior), then travels horizontally at depth.
    private bool _isAquatic;
    private bool _submergePhase;
    private bool _resurfacePhase;
    private float _submergeTargetZ;
    private float _submergeSpeed;
    private float _resurfaceSpeed;
    private float _spawnZ;
    /// <summary>How far below spawn depth the NPC sinks before traveling (meters).</summary>
    private const float SubmergeDepth = 7f;
    /// <summary>Duration of the sinking phase in seconds.</summary>
    private const float SubmergeDuration = 0.7f;
    /// <summary>Duration of the resurface phase in seconds.</summary>
    private const float ResurfaceDuration = 0.7f;

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

        // Aquatic NPCs must stay submerged at their spawn depth.
        // They also sink deeper before traveling (submerge phase).
        if (owner is Npc { IsAquatic: true, CanFly: false } aquaticNpc)
        {
            _isAquatic = true;
            _spawnZ = aquaticNpc.Spawner?.Position.Z ?? owner.Transform.Local.Position.Z;
            _submergeTargetZ = _spawnZ - SubmergeDepth;
            _submergeSpeed = SubmergeDepth / SubmergeDuration;
            _resurfaceSpeed = SubmergeDepth / ResurfaceDuration;
            _endPosition.Z = _submergeTargetZ; // Travel at submerged depth — prevents 3D interpolation from aiming upward
        }
        else
        {
            _isAquatic = false;
            _endPosition.Z = target.Transform.World.Position.Z;
        }

        var distance = MathUtil.CalculateDistance(owner.Transform.World.Position, _endPosition, includeZAxis: !_isAquatic);
        _calculatedSpeed = distance / (Duration / 1000f);
    }

    public void Tick(TimeSpan delta)
    {
        if (Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep) || Owner.IsDead)
        {
            End();
            return;
        }

        // ── Aquatic submerge phase ──
        // During this phase the NPC sinks straight down before traveling horizontally.
        if (_submergePhase)
        {
            var dt = (float)(delta.TotalMilliseconds / 1000.0);
            var sinkDist = _submergeSpeed * dt;
            var currentPos = Owner.Transform.Local.Position;
            var newZ = currentPos.Z - sinkDist;

            if (newZ <= _submergeTargetZ)
            {
                newZ = _submergeTargetZ;
                _submergePhase = false;
            }

            Owner.Transform.Local.SetPosition(currentPos.X, currentPos.Y, newZ);
            SendVerticalPacket(newZ, sinking: true);
            return;
        }

        // ── Aquatic resurface phase ──
        // After horizontal travel, the NPC rises back to spawn depth smoothly.
        if (_resurfacePhase)
        {
            var dt = (float)(delta.TotalMilliseconds / 1000.0);
            var riseDist = _resurfaceSpeed * dt;
            var currentPos = Owner.Transform.Local.Position;
            var newZ = currentPos.Z + riseDist;

            if (newZ >= _spawnZ)
            {
                newZ = _spawnZ;
                _resurfacePhase = false;
                Owner.Transform.Local.SetPosition(currentPos.X, currentPos.Y, newZ);
                Cleanup();
                return;
            }

            Owner.Transform.Local.SetPosition(currentPos.X, currentPos.Y, newZ);
            SendVerticalPacket(newZ, sinking: false);
            return;
        }

        MoveTowards(_calculatedSpeed * (float)(delta.TotalMilliseconds / 1000f));
    }

    /// <summary>Sends a movement packet showing the NPC moving vertically (sinking or rising).</summary>
    private void SendVerticalPacket(float z, bool sinking)
    {
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        var pos = Owner.Transform.Local.Position;
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = pos.X;
        moveType.Y = pos.Y;
        moveType.Z = z;
        moveType.VelX = 0;
        moveType.VelY = 0;
        moveType.VelZ = sinking ? (short)-2000 : (short)2000;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = 4;
        moveType.Flags = MoveTypeFlags.Moving;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = sinking ? (sbyte)-127 : (sbyte)127;
        moveType.Stance = 0;
        moveType.Alertness = MoveTypeAlertness.Combat;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
    }

    public override void Execute()
    {
        base.Execute();
        // For aquatic NPCs, start in submerge phase — the boss sinks first, then travels
        _submergePhase = _isAquatic;
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
    }

    public override void End()
    {
        base.End(); // Mark controller state as ended (unblocks CanUseSkill)

        // For aquatic NPCs: start a smooth resurface animation instead of snapping.
        // The tick handler continues to run during the resurface phase.
        if (_isAquatic && !_resurfacePhase && !_submergePhase)
        {
            _resurfacePhase = true;
            return; // Tick keeps running to animate the rise
        }

        Cleanup();
    }

    /// <summary>Final cleanup — unsubscribe tick, restore position, stop movement.</summary>
    private void Cleanup()
    {
        TickManager.Instance.OnTick.UnSubscribe(Tick);

        if (Owner is Npc npc)
        {
            if (_isAquatic)
            {
                var pos = npc.Transform.Local.Position;
                npc.Transform.Local.SetPosition(pos.X, pos.Y, _spawnZ);
            }
            npc.StopMovement();
        }
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
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, _endPosition, includeZAxis: !_isAquatic);
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

        // Aquatic NPCs: keep Z at the submerged depth during the travel phase.
        // The NPC sank to _submergeTargetZ during the submerge phase and should
        // stay there while traveling horizontally. It surfaces only in End().
        if (Owner is Npc { IsAquatic: true, CanFly: false })
        {
            newZ = _submergeTargetZ;
        }
        else
        {
            var updZ = Owner.ParentWorld.Template.GeoData.GetHeight(Owner.Transform.World.Position);
            if (Math.Abs(newZ - updZ) < 1f)
            {
                newZ = updZ;
            }
        }

        Owner.Transform.Local.SetPosition(newX, newY, newZ);

        var angle = MathUtil.CalculateAngleFrom(Owner.Transform.Local.Position, _endPosition);
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
        Owner.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = Owner.Transform.Local.Position.X;
        moveType.Y = Owner.Transform.Local.Position.Y;
        moveType.Z = Owner.Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        // Aquatic NPCs: VelZ=0 prevents client from interpolating the NPC above water
        moveType.VelZ = (Owner is Npc { IsAquatic: true, CanFly: false }) ? (short)0 : (short)0;
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
