using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class AttackBehavior : Behavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            if (Ai.Owner.CurrentTarget != null)
                Ai.Owner.MoveTowards(Ai.Owner.CurrentTarget.Position, 2.4f * (delta.Milliseconds / 1000.0f));
        }

        public override void Exit()
        {
        }
    }
}
