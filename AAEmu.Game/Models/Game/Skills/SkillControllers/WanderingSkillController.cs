using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Kind 3 <c>wandering</c> (199 <c>skill_controllers</c> rows): the owner is made to run about with no
/// destination — the fear/charm shape. The kind returned null from <c>CreateSkillController</c>, so a fear
/// skill left the NPC standing where the AI had it.
/// </summary>
/// <remarks>
/// value1 is the only column the table fills besides the flag in value10, and its 500..3000 range is read as
/// the length of the wandering in milliseconds: a fear that runs its victim about for a moment and then
/// releases it. The heading is re-picked every time the owner reaches the point it was running to, at the
/// same nominal speed a dash without a duration uses.
/// </remarks>
public class WanderingSkillController : LinearMoveSkillController
{
    /// <summary>Millis the owner wanders when the row names no time.</summary>
    public const int DefaultDurationMs = 2000;

    /// <summary>How far ahead each leg of the wander aims, in metres.</summary>
    public const float LegLength = 2f;

    public int Duration { get; set; }

    /// <summary>When the wandering releases its victim. Settable so a caller can end a fear early.</summary>
    public DateTime EndTime { get; set; }

    public WanderingSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
        : base(template, owner, target)
    {
        Duration = template.Value[0] > 0 ? template.Value[0] : DefaultDurationMs;
        EndTime = DateTime.UtcNow.AddMilliseconds(Duration);
        MoveSpeed = DashSkillController.NominalSpeed;
        ArrivalThreshold = 0.01f;

        PickHeading();
    }

    /// <summary>Aims the next leg in a random direction from where the owner stands.</summary>
    private void PickHeading()
    {
        var yaw = Random.Shared.NextSingle() * MathF.Tau;
        var (endX, endY) = MathUtil.AddDistanceToFront(LegLength, Owner.Transform.Local.Position.X,
            Owner.Transform.Local.Position.Y, yaw);
        EndPosition = new System.Numerics.Vector3(endX, endY, Owner.Transform.Local.Position.Z);
    }

    public override void Tick(TimeSpan delta)
    {
        if (Owner == null || Finished)
            return;

        // A fear that has run its time releases its victim.
        if (DateTime.UtcNow >= EndTime)
        {
            End();
            return;
        }

        // Arrived: keep wandering rather than stopping, which is the difference between this controller and
        // the leap and dash it shares its movement with.
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, EndPosition, true);
        if (targetDist <= ArrivalThreshold)
            PickHeading();

        base.Tick(delta);
    }
}
