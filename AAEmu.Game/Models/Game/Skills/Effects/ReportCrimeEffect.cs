using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ReportCrimeEffect : EffectTemplate
    {
        public int Value { get; set; }
        public uint CrimeKindId { get; set; }

        public override bool OnActionTime => false;
    }
}