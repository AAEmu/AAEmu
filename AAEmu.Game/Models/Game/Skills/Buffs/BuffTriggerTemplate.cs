using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffTriggerTemplate
{
    public uint Id { get; set; }
    public BuffEventTriggerKind Kind { get; set; }
    public EffectTemplate Effect { get; set; }
    public bool UseDamageAmount { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }

    /// <summary>Which unit supplies the triggered effect's source. See <see cref="BuffTriggerAgent"/>.</summary>
    public BuffTriggerAgent SourceAgentId { get; set; }

    /// <summary>Which unit the triggered effect is applied to. See <see cref="BuffTriggerAgent"/>.</summary>
    public BuffTriggerAgent TargetAgentId { get; set; }

    /// <summary>Required on the buff's owner: the owned unit must carry this buff tag.</summary>
    public uint OwnerBuffTagId { get; set; }

    /// <summary>Forbidden on the buff's owner: the owned unit must not carry this buff tag.</summary>
    public uint OwnerNoBuffTagId { get; set; }

    /// <summary>Required on the effect's source unit (see the <c>check_tag_src_in_*</c> flags for when
    /// that is the owner or the target instead).</summary>
    public uint SourceBuffTagId { get; set; }

    /// <summary>Forbidden on the effect's source unit.</summary>
    public uint SourceNoBuffTagId { get; set; }

    /// <summary>
    /// Milliseconds to wait before the triggered effect is applied. 0 applies it inline; negative values
    /// are authored on <c>time</c> rows and mean that far before the buff's end - see
    /// <see cref="BuffTriggerKindRules.ResolveTimeOffsetMs"/>.
    /// </summary>
    public int DelayTime { get; set; }

    /// <summary>
    /// Carry the buff's stack count into the effect as <see cref="BuffTriggerAgentRules.TriggerFullAmount"/>
    /// per stack instead of leaving the amount at zero.
    /// </summary>
    public bool UseStackCount { get; set; }

    /// <summary>Look <see cref="SourceBuffTagId"/> up on the owner rather than on the source.</summary>
    public bool CheckTagSrcInOwner { get; set; }

    /// <summary>Look <see cref="SourceNoBuffTagId"/> up on the owner rather than on the source.</summary>
    public bool CheckNoTagSrcInOwner { get; set; }

    /// <summary>Look <see cref="SourceBuffTagId"/> up on the effect's source. This is what the column name
    /// already means, stated explicitly.</summary>
    public bool CheckTagSrcInSource { get; set; }

    /// <summary>Look <see cref="SourceBuffTagId"/> up on the effect's target instead of on the source.</summary>
    public bool CheckTagSrcInTarget { get; set; }

    /// <summary>Look <see cref="SourceNoBuffTagId"/> up on the effect's source. This is the default.</summary>
    public bool CheckNoTagSrcInSource { get; set; }

    /// <summary>Look <see cref="SourceNoBuffTagId"/> up on the effect's target instead of on the
    /// source.</summary>
    public bool CheckNoTagSrcInTarget { get; set; }

    /// <summary>Pass when any one of the row's unit requirements is met rather than requiring all of
    /// them.</summary>
    public bool OrUnitReqs { get; set; }
}
