using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class RestoreManaEffect : EffectTemplate
    {
        public bool UseFixedValue { get; set; }
        public int FixedMin { get; set; }
        public int FixedMax { get; set; }
        public bool UseLevelValue { get; set; }
        public float LevelMd { get; set; }
        public int LevelVaStart { get; set; }
        public int LevelVaEnd { get; set; }
        public bool Percent { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, DateTime time)
        {
            _log.Debug("RestoreManaEffect");
        }
    }
}
