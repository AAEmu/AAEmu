using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class TrainCraftEffect : EffectTemplate
    {
        public uint CraftId { get; set; }

        public override bool OnActionTime => false;
    }
}