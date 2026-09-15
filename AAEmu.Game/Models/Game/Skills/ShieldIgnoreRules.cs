namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>ignore_shield_chance</c> (<c>enum_unit_attribute</c> 204): the attacker's per-mille chance to land a hit
/// straight on the target's health instead of into its damage-absorption buffs.
/// </summary>
/// <remarks>
/// The name is the client's "방패 관통률" (shield penetration rate) and the rows are authored as tenths of a
/// percent: buff 16763 (아이템_향뜰_한창_일반) stores 10 for "방패 관통률 +1%", buff 16828 stores 50 for "방패
/// 관통률 5% 증가", buff 16780 stores 100 for "방패 관통률 10% 증가", and buff 8227 (양손 무기 착용) stores
/// 500 for the "방패 관통률 50% 증가" its description spells out. The items carry the same scale (39820, 39821
/// and 39822 store 15, 30 and 60). A row of 1000 would be a certain bypass, which is why
/// <see cref="ChanceDenominator"/> is the ceiling and not a percentage of one.
///
/// It is an attacker attribute, not a victim one: every row hangs off the weapon, the ring or the
/// two-handed-weapon buff the attacker is wearing. <c>unit_attribute_limits</c> row 26 (id 204) floors it at
/// 0, so a negative stored value means "never bypass" rather than "shield the target harder".
/// </remarks>
public static class ShieldIgnoreRules
{
    /// <summary>Rolls are out of 1000, matching the per-mille scale <c>unit_modifiers</c> stores.</summary>
    public const int ChanceDenominator = 1000;

    /// <summary>
    /// The stored rating as a usable chance: floored at 0 (never bypass) and capped at
    /// <see cref="ChanceDenominator"/> (always bypass) so a stacked value cannot wrap a roll.
    /// </summary>
    public static int EffectiveChance(long stored) => (int)Math.Clamp(stored, 0, ChanceDenominator);

    /// <summary>
    /// Whether one damage event bypasses the victim's absorption buffs, given the attacker's stored rating and
    /// an already-drawn <paramref name="roll"/> in [0, <see cref="ChanceDenominator"/>).
    /// </summary>
    public static bool BypassesAbsorption(long stored, int roll) => roll < EffectiveChance(stored);
}
