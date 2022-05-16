using System;

using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class HoldPositionBehavior : Behavior
    {
        public override void Enter()
        {
        }
        public override void Tick(TimeSpan delta)
        {
            PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner);
        }

        public override void Exit()
        {
        }
    }
}
