using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class AcceptQuestEffect : EffectTemplate
    {
        public uint QuestId { get; set; }

        public override bool OnActionTime => false;
    }
}