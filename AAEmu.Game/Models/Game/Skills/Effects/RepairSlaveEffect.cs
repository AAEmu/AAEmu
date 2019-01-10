using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class RepairSlaveEffect : EffectTemplate
    {
        public int Health { get; set; }
        public int Mana { get; set; }

        public override bool OnActionTime => false;
    }
}