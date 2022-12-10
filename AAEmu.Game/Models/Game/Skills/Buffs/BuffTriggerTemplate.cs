using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class BuffTriggerTemplate
    {
        public uint Id { get; set; }
        public BuffEventTriggerKind Kind { get; set; } // EventId
        public bool EffectOnSource { get; set; }
        public EffectTemplate Effect { get; set; }
        public bool UseDamageAmount { get; set; }
        public bool UseOriginalSource { get; set; }
        public uint TargetBuffTagId { get; set; }
        public uint TargetNoBuffTagId { get; set; }
        public bool Synergy { get; set; }
        public bool CheckNoTagSrcInOwner { get; set; }
        public bool CheckNoTagSrcInSource { get; set; }
        public bool CheckNoTagSrcInTarget { get; set; }
        public bool CheckTagSrcInOwner { get; set; }
        public bool CheckTagSrcInSource { get; set; }
        public bool CheckTagSrcInTarget { get; set; }
        public uint DelayTime { get; set; }
        public uint OwnerBuffTagId { get; set; }
        public uint OwnerNoBuffTagId { get; internal set; }
        public uint SourceAgentId { get; set; }
        public uint SourceBuffTagId { get; set; }
        public uint SourceNoBuffTagId { get; set; }
        public bool UseCollisionImpact { get; set; }
        public bool UseStackCount { get; set; }
        public uint TargetAgentId { get; set; }
    }
}
