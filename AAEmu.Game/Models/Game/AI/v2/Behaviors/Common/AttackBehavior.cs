using System;

using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class AttackBehavior : BaseCombatBehavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            if (!UpdateTarget() || ShouldReturn)
            {
                Ai.Owner.IsInBattle = false;
                Ai.GoToReturn();
                return;
            }

            if (Ai.Owner.CurrentTarget == null) { return; }
            MoveInRange(Ai.Owner.CurrentTarget, delta);
            if (!CanUseSkill)
            {
                return;
            }

            Ai.Owner.IsInBattle = true;
            PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget);
        }

        public override void Exit()
        {
        }
    }
}
