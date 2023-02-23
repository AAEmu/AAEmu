using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class DeadBehavior : Behavior
    {
        public override void Enter()
        {
            Ai.Owner.InterruptSkills();
            Ai.Owner.StopMovement();
        }

        public override void Tick(TimeSpan delta)
        {
            //if (Ai.Owner.Hp == 0)
            //{
            //    Ai.AlreadyTargetted = false;
            //}
        }

        public override void Exit()
        {
        }
    }
}
