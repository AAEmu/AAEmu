using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class FlyingStateChangeEffect : EffectTemplate
    {
        public bool FlyingState { get; set; }

        public override bool OnActionTime => false;
    }
}