using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ScopedFEffect : EffectTemplate
    {
        public int Range { get; set; }
        public bool Key { get; set; }
        public uint DoodadId { get; set; }

        public override bool OnActionTime => false;
    }
}