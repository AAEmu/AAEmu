using System.Numerics;

using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Kind 2 <c>leap</c>: the owner is thrown to a point <c>value4</c> (thousandths of a metre) beyond the
/// target, along the owner-to-target angle, over <c>value3</c> milliseconds. 2,280 <c>skill_controllers</c>
/// rows are leaps and 125 of the 125 skills that name a controller of this kind are player abilities.
/// </summary>
public class LeapSkillController : LinearMoveSkillController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public int Angle { get; set; }
    public int Speed { get; set; }
    public int Duration { get; set; }
    public int DistanceOffset { get; set; }

    private readonly Vector3 _endPosition;
    public enum LeapDirection
    {
        Both = 0,
        ForwardOnly = 1,
        BackwardOnly = 2
    }
    public LeapDirection Direction { get; set; }

    public LeapSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
        : base(template, owner, target)
    {
        Angle = template.Value[0];
        Speed = template.Value[1];
        Duration = template.Value[2];
        DistanceOffset = template.Value[3];
        Direction = (LeapDirection)template.Value[6];

        var ownerPos = owner.Transform.World.Position;
        var targetPos = target.Transform.World.Position;
        var offset = DistanceOffset / 1000f;
        if (LinearMoveFacingRules.LeapRunsAlongFacing(owner.ObjId, target.ObjId, ownerPos, targetPos))
        {
            // Self-anchored leap: no owner→target direction exists (atan2(0,0) would pin it to world +X),
            // so the offset runs along the owner's facing — backward for a negative row.
            var heading = LinearMoveFacingRules.HeadingAlongFacing(owner.Transform.World.Rotation.Z, offset);
            (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(Math.Abs(offset), ownerPos.X, ownerPos.Y, heading);
            _endPosition.Z = ownerPos.Z;
            FacesTravelDirection = LinearMoveFacingRules.TurnsIntoTravel(offset);
            Logger.Debug("Leap {0}: self-anchored, yaw {1:F1}° offset {2:F2} m → ({3:F1},{4:F1})",
                template.Id, owner.Transform.World.Rotation.Z.RadToDeg(), offset, _endPosition.X, _endPosition.Y);
        }
        else
        {
            var angle = (float)MathUtil.CalculateAngleFrom(ownerPos, targetPos).DegToRad();
            (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(offset, targetPos.X, targetPos.Y, angle);
            _endPosition.Z = targetPos.Z;
        }

        EndPosition = _endPosition;

        // The row's value2 is a speed rating the client uses for its own animation; what the server travels
        // at is the distance the row asks for over the time it gives, so the two never disagree about where
        // the owner lands. A row with no duration would divide by zero, so it is treated as instantaneous
        // (the first tick reaches the end position).
        var distance = MathUtil.CalculateDistance(owner.Transform.World.Position, _endPosition, true);
        MoveSpeed = Duration > 0 ? distance / (Duration / 1000f) : distance * 10f;
    }
}
