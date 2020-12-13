using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class AlmightyNpcAI : AbstractUnitAI
    {
        public AlmightyNpcAI()
        {
            StateMachine.AddState(Framework.States.Idle, new IdleState() {AI = this});
            StateMachine.AddState(Framework.States.AlmightyAttack, new AlmightyAttackState() {AI = this});
        }
        
        public override Framework.States GetNextState(State previous)
        {
            return Framework.States.Idle;
        }
    }
}
