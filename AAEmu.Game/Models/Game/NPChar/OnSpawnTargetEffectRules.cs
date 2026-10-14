using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// When an OnSpawn skill has to be run by the World as a whole skill — through the skill engine —
/// because its effects land on units other than the caster.
/// </summary>
/// <remarks>
/// An event's start script is one OnSpawn skill owned by its start NPC, and its area buff names the
/// unit it lands on through <c>skill_effects.target_buff_tag_id</c> (the tag is read off the
/// *target*, i.e. <c>check_target_tag_src</c> is false). The World's OnSpawn paths cannot deliver
/// that: <see cref="Npc.ApplyOnSpawnSkillBuffs"/> puts every BuffEffect on the caster, and
/// <see cref="OnSpawnPlotWorldGate"/> needs a plot id that a script-style skill does not author. A
/// skill whose tag is redirected at the caster (<c>check_target_tag_src</c> true) is the chain-skill
/// shape that does land on the caster and stays with those paths.
/// </remarks>
public static class OnSpawnTargetEffectRules
{
    /// <summary>One OnSpawn skill effect reduced to the columns that decide who it lands on.</summary>
    public readonly record struct TargetEffect(uint TargetBuffTagId, uint TargetNpcTagId, bool CheckTargetTagSrc);

    /// <summary>
    /// The effects of an OnSpawn skill, in the shape <see cref="ShouldWorldRun"/> reads.
    /// </summary>
    public static IReadOnlyList<TargetEffect> EffectsOf(SkillTemplate template)
    {
        var effects = new List<TargetEffect>();
        if (template?.Effects == null)
            return effects;

        foreach (var effect in template.Effects)
        {
            if (effect == null)
                continue;
            effects.Add(new TargetEffect(effect.TargetBuffTagId, effect.TargetNpcTagId, effect.CheckTargetTagSrc));
        }

        return effects;
    }

    public static bool ShouldWorldRun(
        bool zoneAuthority,
        bool isZoneMirror,
        bool isPriorityMirror,
        bool hasPlot,
        IReadOnlyList<TargetEffect> effects)
    {
        if (!zoneAuthority || !isZoneMirror || !isPriorityMirror)
            return false;
        // A plotted skill is the OnSpawn plot path's job; this path only covers plotless scripts
        // (an event start skill is authored plot_only with no plot id).
        if (hasPlot)
            return false;

        if (effects == null || effects.Count == 0)
            return false;

        foreach (var effect in effects)
        {
            // Read off the caster: the effect is aimed at the caster, not at another unit.
            if (effect.CheckTargetTagSrc)
                continue;
            if (effect.TargetBuffTagId > 0 || effect.TargetNpcTagId > 0)
                return true;
        }

        return false;
    }
}
