using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers
{
    public class SkillController
    {
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
        }

        public virtual void End()
        {
            State = SCState.Ended;
        }

        public static SkillController CreateSkillController(SkillControllerTemplate template, Unit owner, Unit target)
        {
            switch ((SkillControllerKind)template.KindId)
            {
                case SkillControllerKind.Floating:
                    return null;//Todo
                    break;
                case SkillControllerKind.Wandering:
                    return null;//Todo
                    break;
                case SkillControllerKind.Leap:
                    return new LeapSkillController(template, owner, target);
                    break;
                default:
                    return null;
                    break;
            }
        }
    }
}
