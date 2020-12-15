using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class BigMonsterRoamingAI : AbstractUnitAI
    {
        public BigMonsterRoamingAI()
        {
            StateMachine.AddState(Framework.States.Idle, new IdleState());
            StateMachine.AddState(Framework.States.Roaming, new RoamingState());
            StateMachine.AddState(Framework.States.BigMonsterAttack, new BigMonsterAttackState());
            StateMachine.AddState(Framework.States.ReturnToIdle, new ReturnToIdleState());
        }

        public override Framework.States GetNextState(State previous)
        {
            return Framework.States.Idle;
        }
    }
}
