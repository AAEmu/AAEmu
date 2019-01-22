using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class SpecialEffect : EffectTemplate
    {
        public int SpecialEffectTypeId { get; set; } // TODO ... посмотреть что и как
        public int Value1 { get; set; }
        public int Value2 { get; set; }
        public int Value3 { get; set; }
        public int Value4 { get; set; }

        public override bool OnActionTime => false;
    }
}