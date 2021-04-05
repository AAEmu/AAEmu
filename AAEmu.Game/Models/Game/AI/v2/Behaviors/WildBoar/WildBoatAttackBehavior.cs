using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.WildBoar
{
    public class WildBoatAttackBehavior : BaseCombatBehavior
    {
        // onCombatStartSkill = { 15625 }, 
        // onSpurtSkill = {
        //     { skillType = 14038, healthCondition = 70 },
        // },

        private WildBoarAiParams _aiParams;
        private float _prevHealthRatio;
        
        public override void Enter()
        {
            _aiParams = Ai.Param as WildBoarAiParams;
            if (_aiParams == null)
                return;

            _prevHealthRatio = (Ai.Owner.Hp / (float)Ai.Owner.MaxHp) * 100;

            if (!UpdateTarget() || ShouldReturn)
            {
                Ai.GoToReturn();
                return;
            }
            // On Combat Start Skill
            var startCombatSkillId = _aiParams.OnCombatStartSkills.FirstOrDefault();
            if (startCombatSkillId == 0)
                return;

            var skillTemplate = SkillManager.Instance.GetSkillTemplate(startCombatSkillId);
            var skill = new Skill(skillTemplate);
            UseSkill(skill, Ai.Owner.CurrentTarget);
        }

        public override void Tick(TimeSpan delta)
        {
            if (_aiParams == null)
                return;
            
            var healthRatio = (Ai.Owner.Hp / (float)Ai.Owner.MaxHp) * 100;
            
            var target = Ai.Owner.CurrentTarget;
            if (target == null)
                return; // Technically, the aggro code should take us out of this state very soon.
            
            if (CanStrafe)
                MoveInRange(target, delta);

            if (!CanUseSkill)
                return;
            
            // Spurt or base?
        }

        public override void Exit()
        {
        }
    }
}
