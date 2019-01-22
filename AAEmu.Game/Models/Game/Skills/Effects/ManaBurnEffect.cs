using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ManaBurnEffect : EffectTemplate
    {
        public int BaseMin { get; set; }
        public int BaseMax { get; set; }
        public int DamageRatio { get; set; }
        public float LevelMd { get; set; }
        public int LevelVaStart { get; set; }
        public int LevelVaEnd { get; set; }

        public override bool OnActionTime => false;
    }
}