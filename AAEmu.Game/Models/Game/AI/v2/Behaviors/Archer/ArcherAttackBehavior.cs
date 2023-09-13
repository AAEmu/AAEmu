using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Archer
{
    public class ArcherAttackBehavior : BaseCombatBehavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            if (!UpdateTarget() || ShouldReturn)
            {
                Ai.GoToReturn();
                return;
            }
        }

        public override void Exit()
        {
        }
    }
}
