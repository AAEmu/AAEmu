using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which <c>skill_effects</c> rows land when the cast bar starts, not when the skill fires.
/// </summary>
/// <remarks>
/// <c>application_method_id = source_to_pos</c> plus SpecialEffect <c>SpawnDoodad</c> is the
/// cast telegraph (Meteor Strike's ground circle, similar AoE markers). Those rows ship
/// <c>excute_effect_on_fire = f</c> with the rest of the skill's effects; applying every
/// non-fire row at start would also dump damage/buffs early. Scope is the placement doodad only.
/// </remarks>
public static class SkillCastStartEffectRules
{
    /// <summary>
    /// True when this effect must run at cast-start (<c>SCSkillStarted</c>) and be skipped at fire.
    /// </summary>
    public static bool IsCastStartTelegraph(SkillEffect effect) =>
        effect is { ApplicationMethod: SkillEffectApplicationMethod.SourceToPos, Template: SpecialEffect special }
        && special.SpecialEffectTypeId == SpecialType.SpawnDoodad;

    /// <summary>
    /// True when the fire-edge apply pass should run this effect.
    /// </summary>
    /// <param name="castStartTelegraphsApplied">
    /// Whether <c>ApplyCastStartTelegraphs</c> already ran for this cast. When false (instant cast,
    /// or cast-time multipliers that collapse the bar to zero), telegraph rows still apply at fire
    /// so the ground marker is not lost.
    /// </param>
    public static bool AppliesAtFire(SkillEffect effect, bool castStartTelegraphsApplied = false) =>
        !IsCastStartTelegraph(effect) || !castStartTelegraphsApplied;
}
