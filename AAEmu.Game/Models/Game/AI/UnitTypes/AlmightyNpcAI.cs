using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class AlmightyNpcAI : AbstractUnitAI
    {
        public AlmightyNpcAI()
        {
            StateMachine.AddState(Framework.States.Idle, new IdleState() {AI = this});
            StateMachine.AddState(Framework.States.AlmightyAttack, new AlmightyAttackState() {AI = this});
            StateMachine.AddState(Framework.States.ReturnToIdle, new ReturnToIdleState() {AI = this});
            
            StateMachine.SetCurrentState(StateMachine.GetState(Framework.States.Idle));
        }

        public override void OnEnemyDamage(Unit enemy)
        {
            if (StateMachine.GetCurrentState() is AlmightyAttackState aas)
            {
                aas.Target = enemy;
                return;
            }
            
            if (!(StateMachine.GetCurrentState() is IdleState))
                return;

            var attackState = (AlmightyAttackState)StateMachine.GetState(Framework.States.AlmightyAttack);
            attackState.Target = enemy;
            StateMachine.SetCurrentState(attackState);
        }

        public override void OnSkillEnd(Skill skill)
        { 
            if (StateMachine.GetCurrentState() is AlmightyAttackState aas)
                aas.OnSkillEnd(skill);
        }

        public override Framework.States GetNextState(State previous)
        {
            return Framework.States.Idle;
        }
    }
}
