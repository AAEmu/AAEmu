using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class HoldPositionAI : AbstractUnitAI
    {
        public HoldPositionAI()
        {
            StateMachine.AddState(Framework.States.Idle, new IdleState() {AI = this});
            StateMachine.AddState(Framework.States.MovingToTarget, new MoveToTargetState() {AI = this});
            StateMachine.AddState(Framework.States.UsingCombatSkills, new UseCombatSkills() {AI = this});
            
            StateMachine.SetCurrentState(StateMachine.GetState(Framework.States.Idle));
        }

        public override void OnEnemyDamage(Unit enemy)
        {
            if (!(StateMachine.GetCurrentState() is IdleState))
                return;
            
            var state = (MoveToTargetState)StateMachine.GetState(Framework.States.MovingToTarget);
            state.Target = enemy;
            StateMachine.SetCurrentState(state);
        }

        public override void OnEnemySeen(Unit enemy)
        {
            OnEnemyDamage(enemy);
        }

        public override Framework.States GetNextState(State previous)
        {
            return Framework.States.Idle;
        }
    }
}
