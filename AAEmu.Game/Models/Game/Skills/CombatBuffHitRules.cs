namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The rules behind <c>combat_buffs</c>: which hit types a row's <c>hit_type_bits</c> mask covers, and
/// which way round a matched row applies its buff. Pure, so the content can be tested directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mask.</b> <c>hit_type_bits</c> is a bitmask over <c>enum_skill_hit_type</c>, with bit
/// <c>n</c> belonging to enum id <c>n + 1</c> — <c>1 &lt;&lt; (id - 1)</c>. That table's ids are
/// 1 melee_hit, 3 melee_critical, 4 melee_miss, 5 melee_dodge, 6 melee_block, 7 melee_parry,
/// 9 ranged_hit, 10 ranged_miss, 11 ranged_critical, 13 spell_hit, 14 spell_miss, 15 spell_critical,
/// 16 ranged_dodge, 17 ranged_block, 18 immune, 19 spell_resist, 20 ranged_parry. Id 0 invalid has no
/// bit; the gaps at 2, 8 and 12 leave bits 1, 7 and 11 unassigned, which is why
/// <see cref="UnknownBits"/> exists.
/// </para>
/// <para>
/// Odd and even ids both get a bit. The odd-only reading <c>bit = (id - 1) / 2</c> is not possible:
/// the shipped masks reach bit 19 (591205 needs <c>1 &lt;&lt; 19</c> = 524288, and 591104, 591205 and
/// 611685 all carry it), and that layout tops out at bit 9. The content agrees with the wider layout:
/// 32784 = bits 4 and 15 = melee_dodge + ranged_dodge is on 회피 반격 ("evasion counterattack",
/// combat_buffs 16), 8712 = bits 3, 9 and 13 = the three miss results is on 죽음의 응징 (combat_buffs
/// 103), 65568 = bits 5 and 16 = melee_block + ranged_block is on 방패 방어 (combat_buffs 19) and on
/// "the next hit taken is a shield block" (combat_buffs 36), and 524352 = bits 6 and 19 = melee_parry
/// + ranged_parry is on "the next hit taken is a weapon block" (combat_buffs 34).
/// </para>
/// <para>
/// The masks compose additively, which is the tightest check on the layout: 101 (melee_hit,
/// melee_critical, melee_block, melee_parry) | 591104 (the same four ranged outcomes) = 591205
/// exactly, and 591205 | 20480 (spell_hit, spell_critical) = 611685 exactly. Columns that pick the
/// hitting skill confirm it from outside the row names: the skills carrying tag 4749 are all
/// <c>enum_damage_type</c> 1 melee and combat_buffs 51 carries the melee mask 101, tags 4751 and 4752
/// are all spell and carry 20480 and 16384, and hit_skill_id 10434 (원혼 소환, damage_type_id 2
/// spell) carries 20480 on combat_buffs 126.
/// </para>
/// <para>
/// <b>Direction.</b> <c>buff_from_source</c> / <c>buff_to_source</c> say which of the two combatants
/// an entry belongs to: the one that landed the hit (both 't') or the one that took it (both 'f').
/// The shipped content only ever pairs them — 30 rows 't'/'t', 18 'f'/'f', 9 't'/'f', never 'f'/'t' —
/// so the two columns cannot be told apart by the data. This keeps the reading the existing
/// <c>owner == attacker</c> test already encoded, now named: a row fires only in the list of the unit
/// <c>buff_to_source</c> points at, and buffs that unit unless <c>reverse_target_on</c> sends the buff
/// to the other combatant. <c>buff_from_source</c> picks who the applied buff is cast by — the hit's
/// source when it is set, otherwise the unit the buff lands on — which is the same unit on all 57
/// shipped rows.
/// </para>
/// </remarks>
public static class CombatBuffHitRules
{
    private static readonly SkillHitType[] KnownTypes =
        [.. Enum.GetValues<SkillHitType>().Where(type => type != SkillHitType.Invalid)];

    /// <summary>Every bit that names a <see cref="SkillHitType"/>.</summary>
    public static uint KnownBits { get; } = KnownTypes.Aggregate(0u, (bits, type) => bits | Bit(type));

    /// <summary>
    /// The bit <paramref name="type"/> owns in a <c>hit_type_bits</c> mask, or 0 for
    /// <see cref="SkillHitType.Invalid"/> (id 0 has no bit).
    /// </summary>
    public static uint Bit(SkillHitType type)
    {
        var index = (int)type - 1;
        return index is >= 0 and < 32 ? 1u << index : 0u;
    }

    /// <summary>Whether the mask <paramref name="bits"/> covers <paramref name="type"/>.</summary>
    public static bool Sets(uint bits, SkillHitType type) => (bits & Bit(type)) != 0;

    /// <summary>Bits of <paramref name="bits"/> that name no <see cref="SkillHitType"/>.</summary>
    public static uint UnknownBits(uint bits) => bits & ~KnownBits;

    /// <summary>
    /// Expands a mask into the hit types it sets, in enum order. False when the mask sets no known bit
    /// (including 0), in which case the row can never fire and the loader drops it.
    /// </summary>
    public static bool TryDecodeBits(uint bits, out SkillHitType[] types)
    {
        if (bits == 0)
        {
            types = [];
            return false;
        }

        types = [.. KnownTypes.Where(type => Sets(bits, type))];
        return types.Length > 0;
    }

    /// <summary>
    /// Whether a row has to be evaluated in the list of the unit that landed the hit, rather than in
    /// the list of the unit that took it (<c>buff_to_source</c>).
    /// </summary>
    public static bool FiresForOwner(bool buffToSource, bool ownerIsAttacker) => buffToSource == ownerIsAttacker;

    /// <summary>
    /// Whether a matched row buffs the unit it belongs to. <c>reverse_target_on</c> — combat_buffs 138,
    /// 156, 198, 200, 214 and 215 — sends the buff to the other combatant instead. All six carry
    /// buff_from_source and buff_to_source 't', so flipping those two flags would be a no-op and the
    /// column can only be reversing the roles: 독 바르기 (138, 198, 200) puts the poison on the victim of
    /// the hit its own unit landed, and 214/215 put 저주의 씨앗 on the target of the summoned spirit's
    /// spell crit.
    /// </summary>
    public static bool BuffsOwner(bool reverseTargetOn) => !reverseTargetOn;

    /// <summary>
    /// Whether the applied buff is cast by the hit's source. <c>buff_from_source</c> says so directly;
    /// otherwise the buff belongs to the unit it lands on, which is how the 'f'/'f' rows read (방패
    /// 방어 and friends are the defender's own buffs).
    /// </summary>
    public static bool CasterIsAttacker(bool buffFromSource, bool recipientIsAttacker) =>
        buffFromSource || recipientIsAttacker;

    /// <summary>
    /// Whether the hit qualifies for a row that names a skill (<c>hit_skill_id</c>, "the skill that
    /// must have landed") and/or a skill tag (<c>hit_skill_tag_id</c>). A row that names either one
    /// does not match a hit that cannot be attributed to a skill, so effect ticks keep it out.
    /// </summary>
    public static bool MatchesHitSkill(uint hitSkillId, uint hitSkillTagId, uint landedSkillId,
        IReadOnlyCollection<uint> landedSkillTags)
    {
        if (hitSkillId != 0 && hitSkillId != landedSkillId)
            return false;

        if (hitSkillTagId != 0 && (landedSkillTags == null || !landedSkillTags.Contains(hitSkillTagId)))
            return false;

        return true;
    }
}
