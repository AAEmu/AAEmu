using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    enum RoamingStates : uint
    {
        Idle = 0,
        Roaming = 1, // Going to roaming target
        TrackingEnemy = 2, // Going to enemy
        Fighting = 3 // Use spell -> Track
    }
    
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
            StateMachine.AddState((uint) RoamingStates.Idle, new IdleState(3));
            StateMachine.AddState((uint) RoamingStates.Roaming, new RoamingState());
        }

        public override void OnEnemyDamage(Unit enemy)
        {
            // Go to track target state
        }

        public override void OnEnemySeen(Unit enemy)
        {
            // Go to track target state
        }

        public override uint GetNextState(State previous)
        {
            switch (previous)
            {
                case IdleState idleState:
                    return (uint) RoamingStates.Roaming;
                case RoamingState roamingState:
                    return (uint)RoamingStates.Idle;
                default:
                    return (uint) RoamingStates.Idle;
            }
        }
    }
}
