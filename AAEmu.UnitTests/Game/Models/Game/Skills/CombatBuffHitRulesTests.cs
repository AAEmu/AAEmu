using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The layout of <c>combat_buffs.hit_type_bits</c> and the direction rules that go with it.
/// </summary>
/// <remarks>
/// content: game_decrypted.sqlite3 — <c>combat_buffs</c> carries 57 rows with 15 distinct
/// <c>hit_type_bits</c> values, and <c>enum_skill_hit_type</c> ids 1, 3, 4, 5, 6, 7, 9, 10, 11, 13,
/// 14, 15, 16, 17, 18, 19, 20 (plus 0 invalid; 2, 8 and 12 are not in the table). Every mask below is
/// one of those 15, and the expected set is what bit = enum id - 1 decodes it to.
/// </remarks>
public class CombatBuffHitRulesTests
{
    [Test]
    [Arguments(4u, new[] { SkillHitType.MeleeCritical })]
    [Arguments(101u, new[]
    {
        SkillHitType.MeleeHit, SkillHitType.MeleeCritical, SkillHitType.MeleeBlock, SkillHitType.MeleeParry
    })]
    [Arguments(1024u, new[] { SkillHitType.RangedCritical })]
    [Arguments(8712u, new[] { SkillHitType.MeleeMiss, SkillHitType.RangedMiss, SkillHitType.SpellMiss })]
    [Arguments(16384u, new[] { SkillHitType.SpellCritical })]
    [Arguments(17412u, new[] { SkillHitType.MeleeCritical, SkillHitType.RangedCritical, SkillHitType.SpellCritical })]
    [Arguments(20480u, new[] { SkillHitType.SpellHit, SkillHitType.SpellCritical })]
    [Arguments(32784u, new[] { SkillHitType.MeleeDodge, SkillHitType.RangedDodge })]
    [Arguments(65536u, new[] { SkillHitType.RangedBlock })]
    [Arguments(65568u, new[] { SkillHitType.MeleeBlock, SkillHitType.RangedBlock })]
    [Arguments(262144u, new[] { SkillHitType.SpellResist })]
    [Arguments(524352u, new[] { SkillHitType.MeleeParry, SkillHitType.RangedParry })]
    [Arguments(591104u, new[]
    {
        SkillHitType.RangedHit, SkillHitType.RangedCritical, SkillHitType.RangedBlock, SkillHitType.RangedParry
    })]
    [Arguments(591205u, new[]
    {
        SkillHitType.MeleeHit, SkillHitType.MeleeCritical, SkillHitType.MeleeBlock, SkillHitType.MeleeParry,
        SkillHitType.RangedHit, SkillHitType.RangedCritical, SkillHitType.RangedBlock, SkillHitType.RangedParry
    })]
    [Arguments(611685u, new[]
    {
        SkillHitType.MeleeHit, SkillHitType.MeleeCritical, SkillHitType.MeleeBlock, SkillHitType.MeleeParry,
        SkillHitType.RangedHit, SkillHitType.RangedCritical, SkillHitType.SpellHit, SkillHitType.SpellCritical,
        SkillHitType.RangedBlock, SkillHitType.RangedParry
    })]
    public async Task TryDecodeBits_ShippedMask_DecodesToTheHitTypesItSets(uint bits, SkillHitType[] expected)
    {
        var decoded = CombatBuffHitRules.TryDecodeBits(bits, out var types);

        await Assert.That(decoded).IsTrue();
        await Assert.That(types).IsEquivalentTo(expected);
        // No shipped mask reaches into the ids the enum table does not have (bits 1, 7 and 11).
        await Assert.That(CombatBuffHitRules.UnknownBits(bits)).IsEqualTo(0u);
    }

    [Test]
    public async Task Bit_IsTheEnumIdMinusOne_ForOddAndEvenIdsAlike()
    {
        // enum_skill_hit_type ids, one per line, so the layout is read off the table and not a guess.
        await Assert.That(CombatBuffHitRules.Bit(SkillHitType.MeleeHit)).IsEqualTo(1u << 0);        // id 1
        await Assert.That(CombatBuffHitRules.Bit(SkillHitType.MeleeCritical)).IsEqualTo(1u << 2);   // id 3
        await Assert.That(CombatBuffHitRules.Bit(SkillHitType.MeleeMiss)).IsEqualTo(1u << 3);       // id 4 (even)
        await Assert.That(CombatBuffHitRules.Bit(SkillHitType.MeleeDodge)).IsEqualTo(1u << 4);      // id 5
        await Assert.That(CombatBuffHitRules.Bit(SkillHitType.RangedParry)).IsEqualTo(1u << 19);    // id 20 (even)
        await Assert.That(CombatBuffHitRules.Bit(SkillHitType.Invalid)).IsEqualTo(0u);              // id 0 has no bit

        // 32784 = 1 << 4 | 1 << 15 sits on 회피 반격, combat_buffs 16.
        await Assert.That(CombatBuffHitRules.Sets(32784u, SkillHitType.MeleeDodge)).IsTrue();
        await Assert.That(CombatBuffHitRules.Sets(32784u, SkillHitType.RangedDodge)).IsTrue();
        // 4 = 1 << 2 sits on "the next attack is a guaranteed critical", combat_buffs 33 and 37.
        await Assert.That(CombatBuffHitRules.Sets(4u, SkillHitType.MeleeCritical)).IsTrue();
        await Assert.That(CombatBuffHitRules.Sets(4u, SkillHitType.MeleeHit)).IsFalse();
        await Assert.That(CombatBuffHitRules.Sets(4u, SkillHitType.Invalid)).IsFalse();
    }

    [Test]
    public async Task TheShippedCompositeMasks_AreTheUnionOfTheirParts()
    {
        // 101 | 591104 = 591205 and 591205 | 20480 = 611685 hold exactly: the shipped "physical" mask is
        // the melee group plus the ranged one, and the "any landed hit" mask adds the spell group. That
        // additivity is what rules out an arbitrary or odd-ids-only bit order.
        CombatBuffHitRules.TryDecodeBits(101u, out var melee);
        CombatBuffHitRules.TryDecodeBits(591104u, out var ranged);
        CombatBuffHitRules.TryDecodeBits(20480u, out var spell);
        CombatBuffHitRules.TryDecodeBits(591205u, out var physical);
        CombatBuffHitRules.TryDecodeBits(611685u, out var all);

        // Re-encode the decoded sets and union them, so the check runs on the bit values too.
        await Assert.That(Rebuild(melee) | Rebuild(ranged)).IsEqualTo(591205u);
        await Assert.That(Rebuild(physical) | Rebuild(spell)).IsEqualTo(611685u);

        await Assert.That(physical).IsEquivalentTo(melee.Union(ranged));
        await Assert.That(all).IsEquivalentTo(physical.Union(spell));
    }

    private static uint Rebuild(SkillHitType[] types) =>
        types.Aggregate(0u, (bits, type) => bits | CombatBuffHitRules.Bit(type));

    [Test]
    public async Task KnownBits_CoversEveryShippedEnumId_AndLeavesTheThreeGapsUnnamed()
    {
        uint[] shippedIds = [1, 3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19, 20];

        var withoutABit = shippedIds
            .Where(id => !CombatBuffHitRules.Sets(CombatBuffHitRules.KnownBits, (SkillHitType)id))
            .ToArray();

        await Assert.That(withoutABit).IsEmpty();
        await Assert.That(CombatBuffHitRules.UnknownBits(CombatBuffHitRules.KnownBits)).IsEqualTo(0u);

        // Ids 2, 8 and 12 are not in the table, so their bits name nothing.
        foreach (var gap in new[] { 2, 8, 12 })
            await Assert.That(CombatBuffHitRules.Sets(CombatBuffHitRules.KnownBits, (SkillHitType)gap)).IsFalse();
    }

    [Test]
    public async Task TryDecodeBits_Zero_NamesNoHitType()
    {
        await Assert.That(CombatBuffHitRules.TryDecodeBits(0u, out var types)).IsFalse();
        await Assert.That(types).IsEmpty();
        await Assert.That(CombatBuffHitRules.UnknownBits(0u)).IsEqualTo(0u);
    }

    [Test]
    public async Task TryDecodeBits_OnlyUnnamedBits_IsNotDecodable()
    {
        // Bit 1 would be enum id 2, which the table does not have.
        const uint unnamed = 1u << 1;

        await Assert.That(CombatBuffHitRules.TryDecodeBits(unnamed, out var types)).IsFalse();
        await Assert.That(types).IsEmpty();
        await Assert.That(CombatBuffHitRules.UnknownBits(unnamed)).IsEqualTo(unnamed);
    }

    [Test]
    public async Task TryDecodeBits_KnownAndUnnamedBits_KeepsTheKnownOnes()
    {
        var bits = 17412u | (1u << 1); // the any-critical mask plus one bit that names nothing

        await Assert.That(CombatBuffHitRules.TryDecodeBits(bits, out var types)).IsTrue();
        await Assert.That(types).IsEquivalentTo([
            SkillHitType.MeleeCritical, SkillHitType.RangedCritical, SkillHitType.SpellCritical
        ]);
        await Assert.That(CombatBuffHitRules.UnknownBits(bits)).IsEqualTo(1u << 1);
    }

    [Test]
    public async Task FiresForOwner_FollowsBuffToSource()
    {
        // buff_to_source says which side of the hit the entry belongs to: the one that landed it (t) or
        // the one that took it (f). The content only ever pairs the two flags: 30 rows 't'/'t',
        // 18 'f'/'f', 9 't'/'f'.
        await Assert.That(CombatBuffHitRules.FiresForOwner(buffToSource: true, ownerIsAttacker: true)).IsTrue();
        await Assert.That(CombatBuffHitRules.FiresForOwner(buffToSource: true, ownerIsAttacker: false)).IsFalse();
        await Assert.That(CombatBuffHitRules.FiresForOwner(buffToSource: false, ownerIsAttacker: false)).IsTrue();
        await Assert.That(CombatBuffHitRules.FiresForOwner(buffToSource: false, ownerIsAttacker: true)).IsFalse();
    }

    [Test]
    public async Task BuffsOwner_IsFlippedByReverseTargetOn()
    {
        await Assert.That(CombatBuffHitRules.BuffsOwner(reverseTargetOn: false)).IsTrue();
        await Assert.That(CombatBuffHitRules.BuffsOwner(reverseTargetOn: true)).IsFalse();
    }

    [Test]
    public async Task MatchesHitSkill_RequiresTheSkillAndTagTheRowNames()
    {
        // combat_buffs 141 names tag 4751 (skills 10201, 11441, 36620, 36623, 40785, 50985) and no skill.
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(0, 4751, 11441, [4751])).IsTrue();
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(0, 4751, 11441, [])).IsFalse();
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(0, 4751, 11441, [4750])).IsFalse();
        // A hit with no skill behind it (a buff tick) cannot satisfy a tag either.
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(0, 4751, 0, [])).IsFalse();

        // combat_buffs 126 names hit_skill_id 10434 (원혼 소환) and no tag.
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(10434, 0, 10434, [])).IsTrue();
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(10434, 0, 3933, [])).IsFalse();

        // Rows that name neither match every hit.
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(0, 0, 0, [])).IsTrue();
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(0, 0, 12345, [])).IsTrue();

        // Both named: the landed skill has to satisfy both.
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(10434, 4751, 10434, [4751])).IsTrue();
        await Assert.That(CombatBuffHitRules.MatchesHitSkill(10434, 4751, 10434, [4750])).IsFalse();
    }
}
