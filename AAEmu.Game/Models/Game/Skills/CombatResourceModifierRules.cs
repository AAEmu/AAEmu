namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// How <c>unit_modifiers</c> rows with <c>owner_type='CombatResource'</c> scale with the pool amount.
/// </summary>
/// <remarks>
/// Those rows ship <c>value</c> 0 and put the per-point rate in <c>linear_level_bonus</c>
/// (광란 id 1 → attack_speed_mul 3000, melee_critical_bonus 5000). The same
/// <see cref="BuffStackRules.ScaledModifier"/> formula used by buff rows, with the pool amount as
/// <c>abLevel</c> and stack 1, yields +30 attack-speed and +50 crit per delirium point — the
/// numbers the client sheet already shows from its own copy of the table. Without applying these
/// rows the server GCD stays at the bare rating while the HUD reads hundreds higher.
/// </remarks>
public static class CombatResourceModifierRules
{
    /// <summary>Dedicated bonus slot so pool stats do not collide with gear or buffs.</summary>
    public const uint BonusIndex = 0x43524D44;

    /// <summary>
    /// Flat contribution of one CombatResource modifier row at the given pool amount.
    /// Amount 0 (or less) contributes nothing — the bar is empty.
    /// </summary>
    public static long ScaledModifier(long value, int linearLevelBonus, int amount)
    {
        if (amount <= 0)
            return 0;
        return BuffStackRules.ScaledModifier(value, linearLevelBonus, (uint)amount, stack: 1);
    }
}
