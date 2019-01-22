using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class ApplySkillTask : Task
    {
        private Skill _skill;
        private Unit _caster;
        private SkillAction _casterAction;
        private BaseUnit _target;
        private SkillAction _targetAction;

        public ApplySkillTask(Skill skill, Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction)
        {
            _skill = skill;
            _caster = caster;
            _casterAction = casterAction;
            _target = target;
            _targetAction = targetAction;
        }

        public override void Execute()
        {
            _skill.Apply(_caster, _casterAction, _target, _targetAction);
        }
    }
}