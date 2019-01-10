using AAEmu.Game.Models.Game.Skills.Templates;

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
    }
}