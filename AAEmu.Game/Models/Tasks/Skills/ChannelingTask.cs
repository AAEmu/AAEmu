using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class ChannelingTask : SkillTask
    {
        private Unit _caster;
        private SkillAction _casterAction;
        private BaseUnit _target;
        private SkillAction _targetAction;

        public ChannelingTask(Skill skill, Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction) : base(skill)
        {
            _caster = caster;
            _casterAction = casterAction;
            _target = target;
            _targetAction = targetAction;
        }

        public override void Execute()
        {
            Skill.Channeling(_caster, _casterAction, _target, _targetAction);
        }
    }
}