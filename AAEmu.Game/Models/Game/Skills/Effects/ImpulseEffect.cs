using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ImpulseEffect : EffectTemplate
    {
        public float VelImpulseX { get; set; }
        public float VelImpulseY { get; set; }
        public float VelImpulseZ { get; set; }
        public float AngvelImpulseX { get; set; }
        public float AngvelImpulseY { get; set; }
        public float AngvelImpulseZ { get; set; }
        public float ImpulseX { get; set; }
        public float ImpulseY { get; set; }
        public float ImpulseZ { get; set; }
        public float AngImpulseX { get; set; }
        public float AngImpulseY { get; set; }
        public float AngImpulseZ { get; set; }

        public override bool OnActionTime => false;
    }
}