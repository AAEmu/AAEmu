using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public class BuffTrigger
    {
        public uint BuffId { get; set; }
        public BuffTriggerEventKind EventKind { get; set; }
        public bool EffectOnSource { get; set; }
        public uint EffectId { get; set; }
        public bool UseDamageAmount { get; set; }
        public bool UseOriginalSource { get; set; }
        public uint TargetBuffTagId { get; set; }
        public uint TargetNoBuffTagId { get; set; }
        public bool Synergy { get; set; }
    }
}
