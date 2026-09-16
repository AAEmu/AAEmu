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
        Logger.Trace($"SkillController: Npc {Owner.Name}:{Owner.ObjId} entering end state={State}");
    }

    public static SkillController CreateSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        if (template == null)
        {
            return null;
        }

        switch ((SkillControllerKind)template.KindId)
        {
            case SkillControllerKind.Floating:
                Logger.Trace($"SkillController: create FloatingSkillController");
                return new FloatingSkillController(template, owner, target) { State = SCState.Created };
            case SkillControllerKind.Wandering:
                Logger.Trace($"SkillController: create WanderingSkillController");
                return new WanderingSkillController(template, owner, target) { State = SCState.Created };
            case SkillControllerKind.Leap:
                Logger.Trace($"SkillController: create LeapSkillController");
                var ctrl = new LeapSkillController(template, owner, target) { State = SCState.Created };
                return ctrl;
            case SkillControllerKind.Dash:
                Logger.Trace($"SkillController: create DashSkillController");
                return new DashSkillController(template, owner, target) { State = SCState.Created };
            default:
                // The remaining kinds are named but have no controller on this server: rope is handled
                // outside the controller system by ShipHarpoonRopeController (kind 5) and rope_ready (9) is
                // its preparation step, while anchor/rotate/flowgraph/crawl are animation or hull kinds.
                // Naming the kind keeps a new row from looking like an unknown id in the log.
                Logger.Debug("SkillController: kind {0} ({1}) has no controller on this server",
                    template.KindId, (SkillControllerKind)template.KindId);
                return null;
        }
    }
}
