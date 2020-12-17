using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class SpawningBehavior : Behavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            // TODO: Do it properly
            Ai.GoToIdle();
        }

        public override void Exit()
        {
            throw new NotImplementedException();
        }
    }
}
