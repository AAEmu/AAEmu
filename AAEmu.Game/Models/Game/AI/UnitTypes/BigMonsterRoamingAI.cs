using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.States;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class BigMonsterRoamingAI : AbstractUnitAI
    {
        public BigMonsterRoamingAI()
        {
            StateMachine.AddState(Framework.States.Idle, new IdleState() {AI = this});
            StateMachine.AddState(Framework.States.Roaming, new RoamingState(){AI = this});
            StateMachine.AddState(Framework.States.BigMonsterAttack, new BigMonsterAttackState(){AI = this});
            StateMachine.AddState(Framework.States.ReturnToIdle, new ReturnToIdleState(){AI = this});
            
            StateMachine.SetCurrentState(StateMachine.GetState(Framework.States.Idle));
        }

        public override Framework.States GetNextState(State previous)
        {
            return Framework.States.Idle;
        }

        public override void OnEnemyDamage(Unit enemy)
        {
            if (StateMachine.GetCurrentState() is BigMonsterAttackState aas)
            {
                aas.Target = enemy;
                return;
            }

            if (!(StateMachine.GetCurrentState() is IdleState))
                return;

            var attackState = (BigMonsterAttackState)StateMachine.GetState(Framework.States.BigMonsterAttack);
            attackState.Target = enemy;
            StateMachine.SetCurrentState(attackState);
        }

        public override void OnSkillEnd(Skill skill)
        {
            if (StateMachine.GetCurrentState() is BigMonsterAttackState aas)
                aas.OnSkillEnd(skill);
        }
    }
}
