using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class KillNpcWithoutCorpseEffect : EffectTemplate
    {
        public uint NpcId { get; set; }
        public float Radius { get; set; }
        public bool GiveExp { get; set; }
        public bool Vanish { get; set; }

        public override bool OnActionTime => false;
    }
}