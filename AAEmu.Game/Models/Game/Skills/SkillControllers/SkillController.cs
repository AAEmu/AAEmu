using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class SkillController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public enum SCState
    {
        Created,
        Running,
        Ended
    }
    public SkillControllerTemplate Template { get; set; }
    public Unit Owner { get; protected set; }
    public Unit Target { get; protected set; }

    public SCState State { get; protected set; }

    // Set when a SkillController is created from a buff so CC checks inside
    // the controller can ignore the buff that started its own displacement.
    public uint SourceBuffId { get; set; }

    protected SkillController()
    {

    }

    public virtual void Execute()
    {
        State = SCState.Running;
        Logger.Trace($"SkillController: Npc {Owner.Name}:{Owner.ObjId} entering execute state={State}");
    }

    public virtual void End()
    {
        State = SCState.Ended;
        // Owner can be null on this path — Floating's Tick / Wandering's Tick
        // both reach End() via "if (Owner == null) { End(); return; }" when the
        // owning NPC was removed mid-flight. Skip the back-reference cleanup
        // and the trace below in that case.
        if (Owner == null)
            return;
        if (Owner.ActiveSkillController == this)
            Owner.ActiveSkillController = null;
        Logger.Trace($"SkillController: Npc {Owner.Name}:{Owner.ObjId} entering end state={State}");
    }

    public static SkillController CreateSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target,
        float psychokinesisSpeed = 0f, float liftHeight = 0f, float liftSpeed = 0f, float liftDuration = 0f)
    {
        if (template == null)
        {
            return null;
        }

        switch ((SkillControllerKind)template.KindId)
        {
            case SkillControllerKind.Floating:
                Logger.Trace($"SkillController: create FloatingSkillController");
                return new FloatingSkillController(template, owner, target,
                    psychokinesisSpeed, liftHeight, liftSpeed, liftDuration) { State = SCState.Created };
            case SkillControllerKind.Wandering:
                Logger.Trace($"SkillController: create WanderingSkillController");
                return new WanderingSkillController(template, owner, target) { State = SCState.Created };
            case SkillControllerKind.Leap:
                Logger.Trace($"SkillController: create LeapSkillController");
                return new LeapSkillController(template, owner, target) { State = SCState.Created };
            case SkillControllerKind.Dash:
                Logger.Trace($"SkillController: create DashSkillController");
                return new DashSkillController(template, owner, target) { State = SCState.Created };
            default:
                Logger.Trace($"SkillController: create defaultSkillController kind={template.KindId}");
                return null;
        }
    }
}
