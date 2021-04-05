using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class RoamingAI : AbstractUnitAI
    {
        /*
         * The roaming AI has 4 goals:
         * - Move to target when in combat
         * - Use skills when in combat
         * - Roam when not in combat
         *
         * The roaming should also work when working with formations, that is, pack of mobs walking together.
         */

        public RoamingAI()
        {
            StateMachine.AddState(Framework.States.Idle, new IdleState(3));
            StateMachine.AddState(Framework.States.Roaming, new RoamingState());
        }

        public override void OnEnemyDamage(Unit enemy)
        {
            // Go to track target state
        }

        public override void OnEnemySeen(Unit enemy)
        {
            // Go to track target state
        }

        public override Framework.States GetNextState(State previous)
        {
            switch (previous)
            {
                case IdleState idleState:
                    return Framework.States.Roaming;
                case RoamingState roamingState:
                    return Framework.States.Idle;
                default:
                    return Framework.States.Idle;
            }
        }
    }
}
