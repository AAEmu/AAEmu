using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

public class SkillEffect
{
    public uint EffectId { get; set; }
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
    /// <summary>Lower bound of the target's <see cref="TargetCombatResourceId"/> pool for this effect.</summary>
    public int StartCombatResource { get; set; }
    /// <summary>Upper bound of the target's <see cref="TargetCombatResourceId"/> pool for this effect.</summary>
    public int EndCombatResource { get; set; }
    /// <summary>The target's combat resource pool the band applies to; 0 means the effect is not gated.</summary>
    public uint TargetCombatResourceId { get; set; }
    /// <summary>Execute the effect when the skill fires. This server only ever applies effects there.</summary>
    public bool ExcuteEffectOnFire { get; set; }
    public int StartCastingUseChance { get; set; } = 1;
    public int EndCastingUseChance { get; set; } = 100;
    /// <summary>Check <see cref="TargetBuffTagId"/> against the caster instead of the target.</summary>
    public bool CheckTargetTagSrc { get; set; }
    /// <summary>Check <see cref="TargetNoBuffTagId"/> against the caster instead of the target.</summary>
    public bool CheckNoTargetTagSrc { get; set; }
    /// <summary>Stack band of the caster's buffs carrying <see cref="SourceBuffTagId"/>.</summary>
    public int SourceBuffStackCountMin { get; set; }
    /// <inheritdoc cref="SourceBuffStackCountMin"/>
    public int SourceBuffStackCountMax { get; set; }
    /// <summary>Stack band of the target's buffs carrying <see cref="TargetBuffTagId"/>.</summary>
    public int TargetBuffStackCountMin { get; set; }
    /// <inheritdoc cref="TargetBuffStackCountMin"/>
    public int TargetBuffStackCountMax { get; set; }
    /// <summary>Stack band of the caster's buffs that do NOT carry <see cref="SourceBuffTagId"/>.</summary>
    public int SourceExceptBuffStackCountMin { get; set; }
    /// <inheritdoc cref="SourceExceptBuffStackCountMin"/>
    public int SourceExceptBuffStackCountMax { get; set; }
    /// <summary>Stack band of the target's buffs that do NOT carry <see cref="TargetBuffTagId"/>.</summary>
    public int TargetExceptBuffStackCountMin { get; set; }
    /// <inheritdoc cref="TargetExceptBuffStackCountMin"/>
    public int TargetExceptBuffStackCountMax { get; set; }
}
