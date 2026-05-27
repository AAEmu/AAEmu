using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class LeapSkillController : SkillController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public int Duration { get; set; }
    public int DistanceOffset { get; set; }

    private readonly float _calculatedSpeed;
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

        // template.Value[0] (Angle) and template.Value[1] (Speed) are intentionally
        // ignored: the leap direction comes from the caster -> target bearing with
        // a sign flip via DistanceOffset, and the speed is derived from
        // DistanceOffset / Duration so the leap lands in the configured time.
        Duration = template.Value[2];
        DistanceOffset = template.Value[3];
        Direction = (LeapDirection)template.Value[6];

        var ownerPos = owner.Transform.World.Position;
        var targetPos = target.Transform.World.Position;
        var distToTarget = MathUtil.CalculateDistance(ownerPos, targetPos, true);

        // CalculateAngleFrom returns DEGREES; sign of DistanceOffset distinguishes pull vs push.
        var angleDeg = (float)MathUtil.CalculateAngleFrom(ownerPos, targetPos);
        var angleRad = (float)(angleDeg * Math.PI / 180.0);
        var offsetMeters = DistanceOffset / 1000f;

        (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(
            offsetMeters, targetPos.X, targetPos.Y, angleRad);
        _endPosition.Z = targetPos.Z;

        var constrained = ApplyDirectionConstraint(Direction, ownerPos, _endPosition, targetPos);
        if (constrained != _endPosition)
        {
            Logger.Debug("LeapSC: owner={0} {1} constraint violated — staying at owner position",
                owner.ObjId, Direction);
            _endPosition = constrained;
        }

        var distance = MathUtil.CalculateDistance(ownerPos, _endPosition, true);
        var durationSec = Duration > 0 ? Duration / 1000f : 0.5f;
        _calculatedSpeed = distance / durationSec;

        Logger.Debug("LeapSC: owner={0} target={1} dist={2:F1} offset={3:F1}m dir={4} dur={5}ms endPos=({6:F1},{7:F1},{8:F1}) speed={9:F1}",
            owner.ObjId, target.ObjId, distToTarget, offsetMeters, Direction, Duration,
            _endPosition.X, _endPosition.Y, _endPosition.Z, _calculatedSpeed);
    }

    public void Tick(TimeSpan delta)
    {
        if (Owner == null)
        {
            Logger.Warn("LeapSC.Tick: Owner is null, ending");
            End();
            return;
        }
        // SourceBuffId > 0 → forced (buff-triggered pull/grip), ignore CC.
        // SourceBuffId == 0 → voluntary leap, external stun/sleep cancels.
        if (Owner.IsDead)
        {
            Logger.Debug("LeapSC.Tick: owner={0} dead, ending", Owner.ObjId);
            End();
            return;
        }
        if (SourceBuffId == 0 && Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep))
        {
            Logger.Debug("LeapSC.Tick: owner={0} stunned/sleeping (voluntary leap), ending", Owner.ObjId);
            End();
            return;
        }
        var moveDist = _calculatedSpeed * (float)(delta.TotalMilliseconds / 1000f);
        MoveTowards(moveDist);
    }

    public override void Execute()
    {
        base.Execute();
        Logger.Debug("LeapSC.Execute: owner={0} target={1} state={2} endPos=({3:F1},{4:F1},{5:F1}) speed={6:F1}",
            Owner?.ObjId, Target?.ObjId, State, _endPosition.X, _endPosition.Y, _endPosition.Z, _calculatedSpeed);
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
                    e.Template.Stun
                    || e.Template.Sleep
                    || e.Template.Root
                    || e.Template.Knockdown
                    || e.Template.Fastened)
                || Owner.IsDead)
            {
                End();
                return;
            }

            // Player-source leap: ends on combat engagement (per Zeromus on PR #1439).
            // Same reasoning as DashSkillController — a player cannot leap while in
            // combat, so the trigger that ends a voluntary leap mid-flight is "entered
            // combat", not "got snared". Hostile snares flag the player into combat
            // via aggro, so the practical outcome on a snare is unchanged.
            if (Owner is Character && Owner.IsInBattle)
            {
                End();
                return;
            }

            // NPC voluntary leap (rare): IsInBattle never changes for an already-
            // fighting NPC, so keep the hard-root gate to prevent a snared NPC from
            // running forever mid-leap. Slow debuffs ride along via the
            // DecreaseMoveSpeed exclusion.
            if (Owner is Npc && (
                Owner.Buffs.CheckBuffsExcludingTags(
                    SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle),
                    [(uint)SkillConstants.DecreaseMoveSpeed]) ||
                Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare))))
            {
                End();
                return;
            }
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, _endPosition, true);
        if (targetDist <= 1f)
        {
            End();
            return;
        }

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        var travelDist = Math.Min(targetDist, distance);

        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Owner.Transform.Local.Position, _endPosition);
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

    /// <summary>
    /// Enforces the leap's directional constraint. ForwardOnly leaps that would land
    /// farther from the target than the owner started (and BackwardOnly leaps that
    /// would land closer) collapse to the owner's current position so the SC ticks
    /// out cleanly without moving the unit. Both is unconstrained.
    /// </summary>
    internal static Vector3 ApplyDirectionConstraint(
        LeapDirection direction, Vector3 ownerPos, Vector3 candidateEnd, Vector3 targetPos)
    {
        var ownerToTarget = MathUtil.CalculateDistance(ownerPos, targetPos, true);
        var endToTarget = MathUtil.CalculateDistance(candidateEnd, targetPos, true);

        switch (direction)
        {
            case LeapDirection.BackwardOnly:
                return endToTarget < ownerToTarget ? ownerPos : candidateEnd;
            case LeapDirection.ForwardOnly:
                return endToTarget > ownerToTarget ? ownerPos : candidateEnd;
            default:
                return candidateEnd;
        }
    }
}
