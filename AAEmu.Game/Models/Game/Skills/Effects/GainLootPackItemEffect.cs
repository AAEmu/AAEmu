using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class GainLootPackItemEffect : EffectTemplate
    {
        public uint LootPackId { get; set; }
        public bool ConsumeSourceItem { get; set; }
        public uint ConsumeItemId { get; set; }
        public int ConsumeCount { get; set; }
        public bool InheritGrade { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time)
        {
            _log.Debug("GainLootPackItemEffect");
        }
    }
}
