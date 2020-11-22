using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills
{
    public class SkillEffect
    {
        public EffectTemplate Template { get; set; }
        public int Weight { get; set; }
        public byte StartLevel { get; set; }
        public byte EndLevel { get; set; }
        public bool Friendly { get; set; }
        public bool NonFriendly { get; set; }
        public uint TargetBuffTagId { get; set; }
        public uint TargetNoBuffTagId { get; set; }
        public uint SourceBuffTagId { get; set; }
        public uint SourceNoBuffTagId { get; set; }
        public int Chance { get; set; }
        public bool Front { get; set; }
        public bool Back { get; set; }
        public uint TargetNpcTagId { get; set; }
        public SkillEffectApplicationMethod ApplicationMethod { get; set; }
        public bool ConsumeSourceItem { get; set; }
        public uint ConsumeItemId { get; set; }
        public int ConsumeItemCount { get; set; }
        public bool AlwaysHit { get; set; }
        public uint ItemSetId { get; set; }
        public bool InteractionSuccessHit { get; set; }
    }
}
