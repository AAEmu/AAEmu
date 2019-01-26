using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class HealEffect : EffectTemplate
    {
        public bool UseFixedHeal { get; set; }
        public int FixedMin { get; set; }
        public int FixedMax { get; set; }
        public bool UseLevelHeal { get; set; }
        public float LevelMd { get; set; }
        public int LevelVaStart { get; set; }
        public int LevelVaEnd { get; set; }
        public bool Percent { get; set; }
        public bool UseChargedBuff { get; set; }
        public uint ChargedBuffId { get; set; }
        public float ChargedMul { get; set; }
        public bool SlaveApplicable { get; set; }
        public bool IgnoreHealAggro { get; set; }
        public float DpsMultiplier { get; set; }
        public uint ActabilityGroupId { get; set; }
        public int ActabilityStep { get; set; }
        public float ActabilityMul { get; set; }
        public float ActabilityAdd { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, DateTime time)
        {
            _log.Debug("HealEffect");
        }
    }
}
