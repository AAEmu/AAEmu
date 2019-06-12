using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class MeleeCastTask : SkillTask
    {
        //private readonly uint _skillId;
        private readonly Unit _caster;
        private readonly SkillCaster _casterCaster;
        private readonly BaseUnit _target;
        private readonly SkillCastTarget _targetCaster;
        private readonly SkillObject _skillObject;

        public MeleeCastTask(Skill skill, Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject) : base(skill)
        {
            _caster = caster;
            _casterCaster = casterCaster;
            _target = target;
            _targetCaster = targetCaster;
            _skillObject = skillObject;
        }

        public override void Execute()
        {
            Skill.Cast(_caster, _casterCaster, _target, _targetCaster, _skillObject);
        }
    }
}
