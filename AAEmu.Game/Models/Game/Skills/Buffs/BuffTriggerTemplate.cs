using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffTriggerTemplate
{
    public uint Id { get; set; }

public bool UseStackCount { get; set; }

public bool UseCollisionImpact { get; set; }

public uint TargetAgentId { get; set; }

public uint SourceNoBuffTagId { get; set; }

public uint SourceBuffTagId { get; set; }

public uint SourceAgentId { get; set; }

public uint OwnerNoBuffTagId { get; set; }

public uint OwnerBuffTagId { get; set; }

public uint EventId { get; set; }

public uint EffectId { get; set; }

public uint DelayTime { get; set; }

public bool CheckTagSrcInTarget { get; set; }

public bool CheckTagSrcInSource { get; set; }

public bool CheckTagSrcInOwner { get; set; }

public bool CheckNoTagSrcInTarget { get; set; }

public bool CheckNoTagSrcInSource { get; set; }

public bool CheckNoTagSrcInOwner { get; set; }

public uint BuffId { get; set; }
    public BuffEventTriggerKind Kind { get; set; }
    public bool EffectOnSource { get; set; }
    public EffectTemplate Effect { get; set; }
    public bool UseDamageAmount { get; set; }
    public bool UseOriginalSource { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
    public bool Synergy { get; set; }
}
