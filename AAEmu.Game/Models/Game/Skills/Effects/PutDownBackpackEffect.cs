using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class PutDownBackpackEffect : EffectTemplate
    {
        public uint BackpackDoodadId { get; set; }

        public override bool OnActionTime => false;
    }
}