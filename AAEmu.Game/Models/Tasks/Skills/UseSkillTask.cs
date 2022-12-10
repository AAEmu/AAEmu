using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class UseSkillTask : Task
    {
        private readonly Skill _skill;
        private readonly Unit _caster;
        private readonly SkillCaster _casterCaster;
        private readonly SkillCastTarget _targetCaster;
        private readonly SkillObject _skillObject;

        public UseSkillTask(Skill skill, Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            _skill = skill;
            _caster = caster;
            _casterCaster = casterCaster;
            _targetCaster = targetCaster;
            _skillObject = skillObject;
        }

        public override void Execute()
        {
            _skill.Use(_caster, _casterCaster, _targetCaster, _skillObject, true);
        }
    }
}
