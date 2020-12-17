using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class RunCommandSetBehavior : Behavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            // TODO: Proper code
            Ai.GoToIdle();
        }

        public override void Exit()
        {
        }
    }
}
