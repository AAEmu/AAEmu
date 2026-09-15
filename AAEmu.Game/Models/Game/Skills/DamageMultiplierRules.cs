using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What a damage effect's victim is, for picking between the anti-NPC and anti-PC output multipliers
/// (<c>enum_unit_attribute</c> 196-198 and 244-246). Pet and summon classes (<c>Slave</c> owned by a
/// player, <c>Mate</c>) are not one of the two victim kinds the content authors split on, and the
/// content DB only carries the two trios, so they fall to <see cref="Other"/> and take no multiplier.
/// </summary>
public enum DamageVictimKind
{
    Other = 0,
    Npc = 1,
    Player = 2
}

/// <summary>
/// The attacker's six anti-kind output multipliers in one value: melee/ranged/spell against an NPC
/// victim and melee/ranged/spell against a player victim. Each is a factor (1000 per-mille authored as
/// 1.0x), already composed by <see cref="Unit"/> from every bonus the attacker carries.
/// </summary>
public readonly record struct AntiKindDamageMultipliers(
    float MeleeAntiNpc,
    float RangedAntiNpc,
    float SpellAntiNpc,
    float MeleeAntiPc,
    float RangedAntiPc,
    float SpellAntiPc)
{
    /// <summary>An attacker that carries no such bonus: every factor is exactly 1.0f.</summary>
    public static AntiKindDamageMultipliers None => new(1f, 1f, 1f, 1f, 1f, 1f);

    public static AntiKindDamageMultipliers From(Unit attacker) => new(
        attacker.MeleeDamageMulAntiNpc,
        attacker.RangedDamageMulAntiNpc,
        attacker.SpellDamageMulAntiNpc,
        attacker.MeleeDamageMulAntiPc,
        attacker.RangedDamageMulAntiPc,
        attacker.SpellDamageMulAntiPc);
}

/// <summary>
/// The anti-NPC / anti-PC half of a damage effect's output multipliers. The combat dice, the critical
/// path, the target's incoming-damage reduction and the weapon/stat composition are not touched here;
/// this only answers "which of the attacker's anti-kind factors applies to this victim and this damage
/// type", so that DamageEffect can multiply the composed damage by it.
/// </summary>
public static class DamageMultiplierRules
{
    /// <summary>
    /// An NPC victim takes the anti-NPC trio and a player victim the anti-PC trio. <c>Npc</c> and
    /// <c>Character</c> are sibling subclasses of <see cref="Unit"/>, so nothing is ever both; the NPC
    /// test comes first and would win if a subclass ever managed it.
    /// </summary>
    public static DamageVictimKind ClassifyVictim(Unit victim) => victim switch
    {
        Npc => DamageVictimKind.Npc,
        Character => DamageVictimKind.Player,
        _ => DamageVictimKind.Other
    };

    /// <summary>
    /// The factor to apply to the composed damage, always 1.0f when the victim kind or the damage type
    /// has no attribute behind it, so an attacker without the bonus keeps its current numbers exactly.
    /// </summary>
    /// <remarks>
    /// Floored at 0. The rows are authored as a per-mille delta (see <see cref="Unit"/>'s getters), and
    /// four shipped buffs sit below -1000: buff 2102, 2104, 25747 and 25907 (<c>위압감</c>, "intimidation")
    /// carry -1500 on each anti-NPC id, which a plain (value + 1000) / 1000 composition turns into -0.5.
    /// A negative factor would not simply zero the hit, it would flip the sign of the damage and
    /// <c>ReduceCurrentHp</c> would heal the victim instead; the reduction stops at zero.
    /// <c>Character.CastTimeMul</c> clamps its composed factor with <c>Math.Max(res, 0f)</c> the same way.
    /// </remarks>
    public static float SelectDamageMultiplier(
        DamageVictimKind victimKind, DamageType damageType, AntiKindDamageMultipliers attackerMultipliers) =>
        Math.Max(0f, SelectByVictimKind(victimKind, damageType, attackerMultipliers));

    private static float SelectByVictimKind(
        DamageVictimKind victimKind, DamageType damageType, AntiKindDamageMultipliers attackerMultipliers) =>
        victimKind switch
        {
            DamageVictimKind.Npc => SelectByDamageType(
                damageType, attackerMultipliers.MeleeAntiNpc, attackerMultipliers.RangedAntiNpc, attackerMultipliers.SpellAntiNpc),
            DamageVictimKind.Player => SelectByDamageType(
                damageType, attackerMultipliers.MeleeAntiPc, attackerMultipliers.RangedAntiPc, attackerMultipliers.SpellAntiPc),
            _ => 1.0f
        };

    /// <summary>
    /// Siege has no anti-kind attribute in the 10.0.2.13 table, and <see cref="DamageType.Heal"/> is not
    /// a damage type; both keep the neutral factor the existing DamageEffect switch gives them.
    /// </summary>
    private static float SelectByDamageType(DamageType damageType, float melee, float ranged, float spell) =>
        damageType switch
        {
            DamageType.Melee => melee,
            DamageType.Ranged => ranged,
            DamageType.Magic => spell,
            _ => 1.0f
        };
}
