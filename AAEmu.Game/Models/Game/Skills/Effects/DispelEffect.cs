using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class DispelEffect : EffectTemplate
    {
        public int DispelCount { get; set; }
        public int CureCount { get; set; }
        public uint BuffTagId { get; set; }

        public override bool OnActionTime => false;
    }
}