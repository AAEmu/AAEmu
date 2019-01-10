using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class RecoverExpEffect : EffectTemplate
    {
        public bool NeedMoney { get; set; }
        public bool NeedLaborPower { get; set; }
        public bool NeedPriest { get; set; }
        // TODO 1.2 // public bool Penaltied { get; set; }

        public override bool OnActionTime => false;
    }
}