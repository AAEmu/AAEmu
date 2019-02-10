using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class CleanupUccEffect : EffectTemplate
    {
        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj, 
            Skill skill, SkillObject skillObject, DateTime time)
        {
            _log.Debug("CleanupUccEffect");
        }
    }
}
