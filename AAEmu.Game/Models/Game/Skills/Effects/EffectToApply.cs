using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class EffectToApply
    {
        public EffectTemplate _effect { get; set; }
        public Unit _caster { get; set; }
        public SkillCaster _skillCaster { get; set; }
        public BaseUnit _target { get; set; }
        public SkillCastTarget _targetCaster { get; set; }
        public CastSkill _castSkill { get; set; }
        public Skill _skill { get; set; }
        public SkillObject _skillObject { get; set; }


        public EffectToApply(EffectTemplate effectToApply, Unit caster, SkillCaster skillCaster, BaseUnit target,
            SkillCastTarget targetCaster, CastSkill castSkill, Skill skill, SkillObject skillObject)
        {
            _effect = effectToApply;
            _caster = caster;
            _skillCaster = skillCaster;
            _target = target;
            _targetCaster = targetCaster;
            _castSkill = castSkill;
            _skill = skill;
            _skillObject = skillObject;
        }

        public void Apply()
        {
            _effect.Apply(_caster, _skillCaster, _target, _targetCaster, _castSkill, _skill, _skillObject, DateTime.Now);
        }
    }
}
