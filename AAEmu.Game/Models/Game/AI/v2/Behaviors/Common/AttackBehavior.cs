using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class AttackBehavior : BaseCombatBehavior
    {
        public override void Enter()
        {
        }

        public override void Tick(TimeSpan delta)
        {
            if (Ai.Owner.CurrentTarget == null)
                return;

            GetInRange(delta);
            PickSkillAndUseIt();
        }

        private void PickSkillAndUseIt()
        {
            // Attack behavior probably only uses base skill ?
            var skillTemplate = SkillManager.Instance.GetSkillTemplate((uint)Ai.Owner.Template.BaseSkillId);
            var skill = new Skill(skillTemplate);

            if (!Ai.Owner.Cooldowns.CheckCooldown(skillTemplate.Id))
                UseSkill(skill, Ai.Owner.CurrentTarget, Ai.Owner.Template.BaseSkillDelay);
        }
        
        private void GetInRange(TimeSpan delta)
        {
            var dist = MathUtil.CalculateDistance(Ai.Owner.Position, Ai.Owner.CurrentTarget.Position);

            if (dist > Ai.Owner.Template.AttackStartRangeScale * Ai.Owner.ModelSize)
                Ai.Owner.MoveTowards(Ai.Owner.CurrentTarget.Position, 5.4f * (delta.Milliseconds / 1000.0f));
            else
                Ai.Owner.StopMovement();
        }

        public override void Exit()
        {
        }
    }
}
