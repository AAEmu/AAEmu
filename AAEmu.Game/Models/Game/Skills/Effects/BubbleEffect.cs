using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class BubbleEffect : EffectTemplate
    {
        public uint KindId { get; set; }

        public override bool OnActionTime => false;
    }
}