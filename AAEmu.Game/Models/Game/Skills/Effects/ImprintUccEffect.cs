using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ImprintUccEffect : EffectTemplate
    {
        public uint ItemId { get; set; }

        public override bool OnActionTime => false;
    }
}