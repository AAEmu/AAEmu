using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class DispelEffect : EffectTemplate
    {
        public int DispelCount { get; set; }
        public int CureCount { get; set; }
        public uint BuffTagId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, DateTime time)
        {
            _log.Debug("DispelEffect");
        }
    }
}
