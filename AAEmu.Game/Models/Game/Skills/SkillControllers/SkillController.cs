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
                return null; // TODO: Add Floating (telekinesis, bubble ?)
            case SkillControllerKind.Wandering:
                Logger.Trace($"SkillController: create WanderingSkillController");
                return null;// TODO: Add Wandering (Fear ?)
            case SkillControllerKind.Leap:
                Logger.Trace($"SkillController: create LeapSkillController");
                var ctrl = new LeapSkillController(template, owner, target) { State = SCState.Created };
                return ctrl;
            default:
                Logger.Trace($"SkillController: create defaultSkillController");
                return null;
        }
    }
}
