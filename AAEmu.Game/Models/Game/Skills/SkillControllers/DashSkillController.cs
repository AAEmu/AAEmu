using System.Numerics;

using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Kind 4 <c>dash</c>: the owner runs along its own facing for a fixed distance, over a fixed time. 287
/// <c>skill_controllers</c> rows carry it and 125 skills use it (the player's Dash toggle among them); the
/// server had no controller for the kind, so a dash was entirely the client's word.
/// </summary>
/// <remarks>
/// <para>
/// The columns are read the way the leap row beside them is read, since the two kinds share the table:
/// <c>value3</c> is the duration in milliseconds and <c>value4</c> the distance in thousandths of a metre,
/// with the sign of <c>value4</c> choosing forward or backward along the facing. Rows that carry neither —
/// 256 of the 287 leave both at 0 and only set <c>value1</c> — are read as a distance in the same
/// thousandths, which is the only other non-zero column and runs 300..10000 across the table.
/// </para>
/// <para>
/// A row with no duration walks at <see cref="NominalSpeed"/>, the speed the 8 m / 2 s rows (7064 and its
/// 22 siblings: value3 2000, value4 -8000) travel at, so a distance-only row still takes a plausible time
/// instead of teleporting.
/// </para>
/// </remarks>
public class DashSkillController : LinearMoveSkillController
{
    /// <summary>Metres per second a row without a duration is travelled at.</summary>
    public const float NominalSpeed = 4f;

    /// <summary>The raw row values, named as the columns are.</summary>
    public int Value1 { get; set; }
    public int Duration { get; set; }
    public int DistanceOffset { get; set; }

    /// <summary>Metres travelled: positive along the facing, negative against it.</summary>
    public float Distance { get; set; }

    /// <summary>Metres per second the run travels at, i.e. the distance over the row's duration.</summary>
    public float Speed => MoveSpeed;

    public DashSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
        : base(template, owner, target)
    {
        Value1 = template.Value[0];
        Duration = template.Value[2];
        DistanceOffset = template.Value[3];

        // A row that names a distance uses it; one that does not falls back to value1 in the same unit.
        var thousandths = DistanceOffset != 0 ? DistanceOffset : Value1;
        Distance = thousandths / 1000f;

        var seconds = Duration > 0
            ? Duration / 1000f
            : Math.Max(Math.Abs(Distance) / NominalSpeed, 0.1f);
        MoveSpeed = Math.Max(Math.Abs(Distance) / seconds, 0.01f);

        // A dash can be shorter than the metre a leap stops within; only the very end of it counts as
        // arrived, or the run would end before it started.
        ArrivalThreshold = 0.01f;

        // A dash is not aimed at anybody: it is a run in the direction the unit already faces. Transform
        // rotation is in radians (the leap controller's SetRotationDegree call and CalculateAngleFrom's
        // RadToDeg both say so), so this is AddDistanceToFront with the unit the helper expects.
        var yaw = owner.Transform.Local.Rotation.Z;
        var (endX, endY) = MathUtil.AddDistanceToFront(Distance, owner.Transform.Local.Position.X,
            owner.Transform.Local.Position.Y, yaw);
        EndPosition = new Vector3(endX, endY, owner.Transform.Local.Position.Z);
    }
}
