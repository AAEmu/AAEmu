using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ConversionEffect : EffectTemplate
    {
        public uint CategoryId { get; set; }
        public uint SourceCategoryId { get; set; }
        public int SourceValue { get; set; }
        public uint TargetCategoryId { get; set; }
        public int TargetValue { get; set; }

        public override bool OnActionTime => false;
    }
}