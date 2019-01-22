using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class PhysicalExplosionEffect : EffectTemplate
    {
        public float Radius { get; set; }
        public float HoleSize { get; set; }
        public float Pressure { get; set; }

        public override bool OnActionTime => false;
    }
}