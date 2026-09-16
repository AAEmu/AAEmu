using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The <c>unit_modifiers</c> rows a heal effect owns (owner_type='HealEffect', 160 rows in 10.0.2.13).
/// 158 of them are attribute 185 <c>heal_critical_mul</c> with the value -2000, which is what the effects
/// attached to them are authored for: a heal that can never land a critical.
/// </summary>
public static class HealEffectRules
{
    /// <summary>The per-mille baseline a <c>*_mul</c> attribute is authored as a delta from.</summary>
    public const long MultiplierBaseline = 1000;

    /// <summary>
    /// The critical multiplier the effect's own rows give it: the 1000 baseline plus every
    /// <c>heal_critical_mul</c> value, over 1000. An effect with no such row is exactly 1.0, and the -2000
    /// the shipped rows carry lands on -1.0, which <see cref="CanCrit"/> reads as "never".
    /// </summary>
    public static double CriticalMultiplier(IEnumerable<BonusTemplate> bonuses)
    {
        if (bonuses == null)
            return 1d;

        var perMille = 0L;
        foreach (var bonus in bonuses)
        {
            if (bonus.Attribute != UnitAttribute.HealCriticalMul)
                continue;
            perMille += bonus.Value;
        }

        return (MultiplierBaseline + perMille) / (double)MultiplierBaseline;
    }

    /// <summary>Whether a heal carrying this multiplier may roll a critical at all.</summary>
    public static bool CanCrit(double criticalMultiplier) => criticalMultiplier > 0d;
}
