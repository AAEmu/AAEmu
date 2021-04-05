using System;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class ReturnToIdleState: State
    {
        public override void Enter()
        {
            // Heal owner to full
            // Apply return buff
        }

        public override void Tick(TimeSpan delta)
        {
            if (!(AI.Owner is Npc npc))
                return;
            
            npc.MoveTowards(AI.IdlePosition, 4.4f * (delta.Milliseconds / 1000.0f));
            if (MathUtil.CalculateDistance(npc.Position, AI.IdlePosition, true) < 1.0f)
            {
                npc.StopMovement();
                GoToIdle();
            }
        }

        public override void Exit()
        {
            // Remove return buff
        }

        private void GoToIdle()
        {
            var idleState = AI.StateMachine.GetState(Framework.States.Idle);
            AI.StateMachine.SetCurrentState(idleState);
        }
    }
}
