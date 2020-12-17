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
            // TODO: This follows the game's way of doing it. This will need code later, obviously
            Ai.GoToRunCommandSet();
        }

        public override void Exit()
        {
        }
    }
}
