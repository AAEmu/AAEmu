using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>enum_combat_dice_kinds</c>: which roll a cast makes against its target.
/// </summary>
public enum CombatDiceKind
{
    Melee = 1,
    Ranged = 2,
    Spell = 3,
    AlwaysHit = 4,
    MeleeUndefendable = 5,
    Heal = 6,
    EachEffectRollDice = 7,
    RangeUndefendable = 8
}

/// <summary>
/// The combat dice a cast rolls, from <c>skills.combat_dice_id</c> (8 kinds) and the skill's damage type.
/// </summary>
/// <remarks>
/// <c>combat_dice_id</c> shipped loaded and unread: <see cref="RollCombatDice"/> branched on
/// <c>damage_type_id</c> alone, so an <c>always_hit</c> skill could miss and a
/// <c>melee_undefendable</c> one could be dodged, parried or blocked. The two columns agree on the
/// damage family almost everywhere — of the 1,656 skills with kind 1 (melee), 1,596 carry damage_type 1;
/// kind 3 (spell) pairs with magic on 1,412 of 1,509; kind 2 (ranged) with ranged on 463 of 488 — so the
/// kind selects <i>which rolls happen</i> and the damage type still decides which hit-type flag the
/// client is told.
///
/// Kind 4 <c>always_hit</c> is the DB default (33,871 of 38,043 skills), which is why most casts in the
/// game never roll: an ordinary instant ability is authored to land.
///
/// Avoidance is skipped when the attacker is behind the target. The old code did this with an
/// "Idk if this is right" note; it is kept, and it is what the kinds describe — a target that cannot see
/// the blow cannot dodge, parry or block it, while kind 4/5/8 are authored to skip avoidance outright
/// regardless of facing.
/// </remarks>
public static class CombatDiceRules
{
    /// <summary>
    /// The kind a cast rolls. An unset kind (0) falls back to the damage type's own family, which is the
    /// behaviour every skill had before this column was read; <c>Siege</c> and <c>Heal</c> have no dice
    /// kind of their own and roll nothing.
    /// </summary>
    public static CombatDiceKind Kind(int combatDiceId, uint damageTypeId)
    {
        if (Enum.IsDefined(typeof(CombatDiceKind), combatDiceId))
            return (CombatDiceKind)combatDiceId;

        return (DamageType)damageTypeId switch
        {
            DamageType.Melee => CombatDiceKind.Melee,
            DamageType.Ranged => CombatDiceKind.Ranged,
            DamageType.Magic => CombatDiceKind.Spell,
            _ => CombatDiceKind.AlwaysHit
        };
    }

    /// <summary>
    /// Whether dodge, parry and block are rolled. The two <c>undefendable</c> kinds skip them, and
    /// <c>always_hit</c> / <c>heal</c> / <c>each_effect_roll_dice</c> are not avoidance rolls either.
    /// </summary>
    public static bool RollsAvoidance(CombatDiceKind kind) =>
        kind is CombatDiceKind.Melee or CombatDiceKind.Ranged or CombatDiceKind.Spell;

    /// <summary>
    /// Whether the cast rolls to land at all. The undefendable kinds still miss — they only refuse to be
    /// dodged, parried or blocked — while <c>always_hit</c> and <c>heal</c> always land.
    /// </summary>
    public static bool RollsMiss(CombatDiceKind kind) =>
        kind is CombatDiceKind.Melee or CombatDiceKind.Ranged or CombatDiceKind.Spell
            or CombatDiceKind.MeleeUndefendable or CombatDiceKind.RangeUndefendable;

    /// <summary>
    /// Whether a cast rolls dice at all. A hostile skill always did; the rest were skipped, so a damage
    /// skill with any other <c>target_type_id</c> could never miss.
    /// </summary>
    public static bool RollsForCast(bool targetTypeIsHostile, bool hasDamageEffect)
        => targetTypeIsHostile || hasDamageEffect;

    /// <summary>The hit type reported when the cast lands without a roll.</summary>
    public static SkillHitType HitTypeFor(uint damageTypeId) => (DamageType)damageTypeId switch
    {
        DamageType.Melee => SkillHitType.MeleeHit,
        DamageType.Magic => SkillHitType.SpellHit,
        DamageType.Ranged => SkillHitType.RangedHit,
        DamageType.Siege => SkillHitType.RangedHit,
        _ => SkillHitType.Invalid
    };

    /// <summary>The hit type reported when the miss roll fails.</summary>
    public static SkillHitType MissTypeFor(uint damageTypeId) => (DamageType)damageTypeId switch
    {
        DamageType.Melee => SkillHitType.MeleeMiss,
        DamageType.Magic => SkillHitType.SpellMiss,
        DamageType.Ranged => SkillHitType.RangedMiss,
        _ => SkillHitType.Invalid
    };

    /// <summary>
    /// The outcome for a caster that is not a Unit and therefore has no accuracy to roll against.
    /// Preserved rather than corrected: the old roll ended in the miss branch for melee, magic and
    /// ranged when the attacker was null, and siege had no miss type of its own and reported a hit.
    /// </summary>
    public static SkillHitType UnrollableSourceType(uint damageTypeId)
    {
        var miss = MissTypeFor(damageTypeId);
        return miss == SkillHitType.Invalid ? HitTypeFor(damageTypeId) : miss;
    }
}
