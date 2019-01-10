using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class OpenPortalEffect : EffectTemplate
    {
        public float Distance { get; set; }

        public override bool OnActionTime => false;
    }
}