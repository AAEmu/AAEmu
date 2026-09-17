using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Kind 1 <c>floating</c> (236 <c>skill_controllers</c> rows): the owner is lifted off the ground and held
/// there — the telekinesis and bubble shape. The kind returned null from <c>CreateSkillController</c>.
/// </summary>
/// <remarks>
/// value1 and value2 are the two columns the table fills, and they are read as the ship's-anchor pair the
/// size of the numbers suggests: value1 the time in milliseconds the lift takes (1,000..20,000) and value2
/// the height in thousandths of a metre (1,000..30,000). The owner is raised over that time, held for the
/// same time again, and set back down where it started when the controller ends, so a lifted unit never
/// keeps a height its world does not know about.
/// </remarks>
public class FloatingSkillController : SkillController
{
    /// <summary>Millis the lift takes when the row names no time.</summary>
    public const int DefaultLiftMs = 3000;

    /// <summary>Thousandths of a metre the owner is lifted when the row names no height.</summary>
    public const int DefaultHeight = 2000;

    public int LiftMs { get; set; }
    public int HeightMm { get; set; }

    /// <summary>Where the owner will be put back down.</summary>
    public float GroundZ { get; private set; }

    private DateTime _startedAt;
    private bool _lifted;

    public FloatingSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target as Unit;

        LiftMs = template.Value[0] > 0 ? template.Value[0] : DefaultLiftMs;
        HeightMm = template.Value[1] > 0 ? template.Value[1] : DefaultHeight;
        GroundZ = owner.Transform.Local.Position.Z;
    }

    public override void Execute()
    {
        base.Execute();
        _startedAt = DateTime.UtcNow;
        Core.Managers.TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
    }

    public override void End()
    {
        base.End();
        Core.Managers.TickManager.Instance.OnTick.UnSubscribe(Tick);

        // Down again where the owner was picked up, and no longer pointing at a finished controller.
        if (Owner != null)
        {
            Owner.Transform.Local.SetHeight(GroundZ);
            if (ReferenceEquals(Owner.ActiveSkillController, this))
                Owner.ActiveSkillController = null;
        }
    }

    public void Tick(TimeSpan delta)
    {
        if (Owner == null || State == SCState.Ended)
            return;

        if (Owner.IsDead)
        {
            End();
            return;
        }

        var elapsed = (DateTime.UtcNow - _startedAt).TotalMilliseconds;
        if (elapsed >= LiftMs * 2.0)
        {
            // Held for as long as the lift took; the row's own buff decides whether anything else happens.
            End();
            return;
        }

        var height = HeightAt(elapsed);
        Owner.Transform.Local.SetHeight(GroundZ + height);
        _lifted = height > 0f;
    }

    /// <summary>
    /// How high above the ground the owner is <paramref name="elapsedMs"/> into the controller's life: the
    /// row's height once the lift is done, held for as long again, then nothing.
    /// </summary>
    public float HeightAt(double elapsedMs)
    {
        if (elapsedMs >= LiftMs * 2.0)
            return 0f;

        var height = HeightMm / 1000f;
        if (elapsedMs < LiftMs)
            height *= (float)(elapsedMs / LiftMs);
        return height;
    }

    /// <summary>Whether the owner is off the ground right now.</summary>
    public bool IsLifted => _lifted;
}
