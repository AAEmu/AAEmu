using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Resolves the content amount a <c>CombatResourceEffect</c> should actually grant.
/// </summary>
/// <remarks>
/// Two class-agnostic content patterns need a server-side prefer:
/// <list type="bullet">
/// <item>
/// Plot-only skills often ship the same pool grant on both <c>skill_effects</c> and
/// <c>plot_effects</c>. The plot row is what runs (plot_only never reaches ApplyEffects), and for a
/// few enhanced variants it carries a larger amount than the skill_effects / synergy-icon row
/// (Mana Bolt under Fury: plot +4 vs skill_effects +1 / "1회씩 누적"). When the skill names a
/// passing skill_effects grant for the same pool, that amount is authoritative.
/// </item>
/// <item>
/// Buff-tick grants keep their authored <c>combat_resource_effects</c> amount. Replacing a
/// ceiling-sized tick with a literal +1 invents a value that is not in content.
/// </item>
/// </list>
/// </remarks>
public static class CombatResourceGrantRules
{
    /// <summary>
    /// Buff-tick path: keep the authored grant amount from content.
    /// </summary>
    public static int ResolveBuffTickAmount(int amount, int resourceMax)
    {
        _ = resourceMax;
        return amount;
    }

    /// <summary>
    /// Plot path: prefer a passing <c>skill_effects</c> grant for the same pool when the plot amount
    /// is not one of those grants.
    /// </summary>
    /// <param name="skillOwnedResourceId">
    /// <c>skills.combat_resource_id</c> — used when a skill_effects CRE leaves
    /// <c>combat_resource_id</c> at 0 ("the resource the skill owns").
    /// </param>
    public static int ResolvePlotAmount(
        int plotAmount,
        int resourceId,
        int skillOwnedResourceId,
        IReadOnlyList<SkillEffect> skillEffects,
        Func<uint, List<uint>> buffsByTag,
        Func<List<uint>, bool> casterHasAnyBuff)
    {
        if (plotAmount <= 0 || skillEffects == null || skillEffects.Count == 0)
            return plotAmount;

        List<int> passingGrants = null;
        foreach (var effect in skillEffects)
        {
            if (!TryPassingGrant(effect, resourceId, skillOwnedResourceId, buffsByTag, casterHasAnyBuff,
                    requireTag: false, out var grant))
                continue;
            (passingGrants ??= []).Add(grant);
        }

        if (passingGrants == null)
            return plotAmount;

        foreach (var grant in passingGrants)
        {
            if (grant == plotAmount)
                return plotAmount;
        }

        // Prefer a tagged (conditional) grant when several pass — those are the enhanced branches.
        if (TryFirstPassingGrant(skillEffects, resourceId, skillOwnedResourceId, buffsByTag, casterHasAnyBuff,
                requireTag: true, out var taggedGrant))
            return taggedGrant;

        return passingGrants[0];
    }

    private static bool TryFirstPassingGrant(
        IReadOnlyList<SkillEffect> skillEffects,
        int resourceId,
        int skillOwnedResourceId,
        Func<uint, List<uint>> buffsByTag,
        Func<List<uint>, bool> casterHasAnyBuff,
        bool requireTag,
        out int grant)
    {
        foreach (var effect in skillEffects)
        {
            if (TryPassingGrant(effect, resourceId, skillOwnedResourceId, buffsByTag, casterHasAnyBuff,
                    requireTag, out grant))
                return true;
        }

        grant = 0;
        return false;
    }

    private static bool TryPassingGrant(
        SkillEffect effect,
        int resourceId,
        int skillOwnedResourceId,
        Func<uint, List<uint>> buffsByTag,
        Func<List<uint>, bool> casterHasAnyBuff,
        bool requireTag,
        out int grant)
    {
        grant = 0;
        if (effect?.Template is not CombatResourceEffect cre)
            return false;

        var effectResourceId = cre.CombatResourceId != 0 ? cre.CombatResourceId : skillOwnedResourceId;
        if (effectResourceId != resourceId)
            return false;

        grant = FixedAmount(cre);
        if (grant <= 0)
            return false;

        if (requireTag && effect.SourceBuffTagId == 0)
            return false;

        if (effect.SourceBuffTagId > 0)
        {
            var tagged = buffsByTag(effect.SourceBuffTagId);
            if (tagged == null || tagged.Count == 0 || !casterHasAnyBuff(tagged))
                return false;
        }

        if (effect.SourceNoBuffTagId > 0)
        {
            var tagged = buffsByTag(effect.SourceNoBuffTagId);
            if (tagged is { Count: > 0 } && casterHasAnyBuff(tagged))
                return false;
        }

        return true;
    }

    private static int FixedAmount(CombatResourceEffect cre) =>
        cre.MinCombatResource == cre.MaxCombatResource
            ? cre.MinCombatResource
            : Math.Min(cre.MinCombatResource, cre.MaxCombatResource);
}
