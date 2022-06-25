using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class CastTask : SkillTask
    {
        private readonly IUnit _caster;
        private readonly SkillCaster _casterCaster;
        private readonly IBaseUnit _target;
        private readonly SkillCastTarget _targetCaster;
        private readonly SkillObject _skillObject;

        public CastTask(Skill skill, IUnit caster, SkillCaster casterCaster, IBaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject) : base(skill)
        {
            _caster = caster;
            _casterCaster = casterCaster;
            _target = target;
            _targetCaster = targetCaster;
            _skillObject = skillObject;
        }

        public override void Execute()
        {
            if (Skill.Cancelled)
                return;
            Skill.Cast(_caster, _casterCaster, _target, _targetCaster, _skillObject);
        }
    }
}
