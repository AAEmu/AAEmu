using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The pure half of the <c>remove_on_*</c> grid, the three columns beside it and <c>buff_breakers</c>.
/// Everything here is a decision the live code only calls; the wiring at the call sites is covered by
/// <see cref="BuffRemoveOnApplyTests"/>.
/// </summary>
public class BuffRemoveOnRulesTests
{
    [Test]
    public async Task HitCause_OrdinaryHit_IsOrdinary()
    {
        await Assert.That(BuffRemoveOnRules.HitCause(isTrigger: false, isDotTick: false, isMagic: false))
            .IsEqualTo(BuffHitCause.Ordinary);
        await Assert.That(BuffRemoveOnRules.HitCause(isTrigger: false, isDotTick: false, isMagic: true))
            .IsEqualTo(BuffHitCause.Ordinary);
    }

    [Test]
    public async Task HitCause_DotTick_SplitsOnTheDamageType()
    {
        await Assert.That(BuffRemoveOnRules.HitCause(false, isDotTick: true, isMagic: true))
            .IsEqualTo(BuffHitCause.SpellDot);
        await Assert.That(BuffRemoveOnRules.HitCause(false, isDotTick: true, isMagic: false))
            .IsEqualTo(BuffHitCause.EtcDot);
    }

    [Test]
    public async Task HitCause_TriggerHit_IsBuffTriggerWhateverElseItLooksLike()
    {
        // A trigger's effect can also be a ticking buff's; the trigger is the narrower fact and wins.
        await Assert.That(BuffRemoveOnRules.HitCause(isTrigger: true, isDotTick: true, isMagic: true))
            .IsEqualTo(BuffHitCause.BuffTrigger);
        await Assert.That(BuffRemoveOnRules.HitCause(isTrigger: true, isDotTick: false, isMagic: false))
            .IsEqualTo(BuffHitCause.BuffTrigger);
    }

    [Test]
    public async Task Flags_OrdinaryHit_RaiseOnlyTheUmbrella()
    {
        await Assert.That(BuffRemoveOnRules.AttackFlags(BuffHitCause.Ordinary))
            .IsEquivalentTo(new[] { BuffRemoveOn.AttackEtc });
        await Assert.That(BuffRemoveOnRules.AttackedFlags(BuffHitCause.Ordinary))
            .IsEquivalentTo(new[] { BuffRemoveOn.AttackedEtc });
        await Assert.That(BuffRemoveOnRules.DamageFlags(BuffHitCause.Ordinary))
            .IsEquivalentTo(new[] { BuffRemoveOn.DamageEtc });
        await Assert.That(BuffRemoveOnRules.DamagedFlags(BuffHitCause.Ordinary))
            .IsEquivalentTo(new[] { BuffRemoveOn.DamagedEtc });
    }

    [Test]
    public async Task Flags_DotTick_RaiseTheUmbrellaAndTheDotMember()
    {
        // 31/31 carriers of remove_on_attack_spell_dot and 86/86 of remove_on_attacked_etc_dot also carry
        // the umbrella, so both are raised: the umbrella keeps every existing removal working and the
        // narrow member is what the ten buffs without an umbrella need.
        await Assert.That(BuffRemoveOnRules.AttackFlags(BuffHitCause.SpellDot))
            .IsEquivalentTo(new[] { BuffRemoveOn.AttackEtc, BuffRemoveOn.AttackSpellDot });
        await Assert.That(BuffRemoveOnRules.AttackFlags(BuffHitCause.EtcDot))
            .IsEquivalentTo(new[] { BuffRemoveOn.AttackEtc, BuffRemoveOn.AttackEtcDot });
        await Assert.That(BuffRemoveOnRules.DamagedFlags(BuffHitCause.SpellDot))
            .IsEquivalentTo(new[] { BuffRemoveOn.DamagedEtc, BuffRemoveOn.DamagedSpellDot });
        await Assert.That(BuffRemoveOnRules.DamagedFlags(BuffHitCause.EtcDot))
            .IsEquivalentTo(new[] { BuffRemoveOn.DamagedEtc, BuffRemoveOn.DamagedEtcDot });
    }

    [Test]
    public async Task Flags_TriggerHit_RaiseTheBuffTriggerMember()
    {
        await Assert.That(BuffRemoveOnRules.AttackFlags(BuffHitCause.BuffTrigger))
            .IsEquivalentTo(new[] { BuffRemoveOn.AttackEtc, BuffRemoveOn.AttackBuffTrigger });
        await Assert.That(BuffRemoveOnRules.AttackedFlags(BuffHitCause.BuffTrigger))
            .IsEquivalentTo(new[] { BuffRemoveOn.AttackedEtc, BuffRemoveOn.AttackedBuffTrigger });
        await Assert.That(BuffRemoveOnRules.DamageFlags(BuffHitCause.BuffTrigger))
            .IsEquivalentTo(new[] { BuffRemoveOn.DamageEtc, BuffRemoveOn.DamageBuffTrigger });
        await Assert.That(BuffRemoveOnRules.DamagedFlags(BuffHitCause.BuffTrigger))
            .IsEquivalentTo(new[] { BuffRemoveOn.DamagedEtc, BuffRemoveOn.DamagedBuffTrigger });
    }

    [Test]
    public async Task IsAutoAttack_TheThreeBasicAttackSkills_AreAutoAttacks()
    {
        // skills 2 근접 공격, 3 Offhand, 4 원거리 공격 — the ids Skill.cs and CSStartSkillPacket test.
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(2)).IsTrue();
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(3)).IsTrue();
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(4)).IsTrue();
    }

    [Test]
    public async Task IsAutoAttack_EveryOtherSkill_IsNot()
    {
        // 0 is what the loader falls back to for a skill with no id, and 1 is the generic attack every
        // other skill is not.
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(0)).IsFalse();
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(1)).IsFalse();
    }

    /// <summary>
    /// The weapon-slot column is NOT the auto-attack marker, which is the bug this replaced: 543 shipped
    /// skills name a slot above zero (490 at 15, one at 16, 47 at 17, five at 18), so reading the column
    /// raised <c>remove_on_autoattack</c> for boss abilities and mount attacks. 10399 방패 휘두르기,
    /// 12619 올려치기, 16287 질주 and 16064 활쏘기 are in that set; 41 of the 146 carrier buffs set no
    /// other skill or attack removal flag, so they would have dropped on a boss ability.
    /// </summary>
    [Test]
    public async Task IsAutoAttack_SkillsThatMerelyNameAWeaponSlot_AreNot()
    {
        // Slot values 15/16/17 belong to the three basic attacks, but 543 rows carry one and are not
        // auto-attacks; the predicate no longer takes the slot at all, so the assertion is that a skill
        // with a slot but an id outside 2/3/4 is not one.
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(10399)).IsFalse();
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(16287)).IsFalse();
        await Assert.That(BuffRemoveOnRules.IsAutoAttack(16064)).IsFalse();
    }

    /// <summary>
    /// One flag per template, and only that flag: this is the whole flag-to-column mapping of the old
    /// if-chain, so a swapped pair anywhere in it shows up here.
    /// </summary>
    private static readonly (BuffRemoveOn On, BuffTemplate Template)[] FlagTemplates =
    [
        (BuffRemoveOn.SourceDead, new BuffTemplate { RemoveOnSourceDead = true }),
        (BuffRemoveOn.UseSkill, new BuffTemplate { RemoveOnUseSkill = true }),
        (BuffRemoveOn.Move, new BuffTemplate { RemoveOnMove = true }),
        (BuffRemoveOn.Death, new BuffTemplate { RemoveOnDeath = true }),
        (BuffRemoveOn.Exempt, new BuffTemplate { RemoveOnExempt = true }),
        (BuffRemoveOn.Land, new BuffTemplate { RemoveOnLand = true }),
        (BuffRemoveOn.Interaction, new BuffTemplate { RemoveOnInteraction = true }),
        (BuffRemoveOn.Unmount, new BuffTemplate { RemoveOnUnmount = true }),
        (BuffRemoveOn.Mount, new BuffTemplate { RemoveOnMount = true }),
        (BuffRemoveOn.Unbond, new BuffTemplate { RemoveOnUnbond = true }),
        (BuffRemoveOn.StartSkill, new BuffTemplate { RemoveOnStartSkill = true }),
        (BuffRemoveOn.AttackSpellDot, new BuffTemplate { RemoveOnAttackSpellDot = true }),
        (BuffRemoveOn.AttackEtcDot, new BuffTemplate { RemoveOnAttackEtcDot = true }),
        (BuffRemoveOn.AttackBuffTrigger, new BuffTemplate { RemoveOnAttackBuffTrigger = true }),
        (BuffRemoveOn.AttackEtc, new BuffTemplate { RemoveOnAttackEtc = true }),
        (BuffRemoveOn.AttackedSpellDot, new BuffTemplate { RemoveOnAttackedSpellDot = true }),
        (BuffRemoveOn.AttackedEtcDot, new BuffTemplate { RemoveOnAttackedEtcDot = true }),
        (BuffRemoveOn.AttackedBuffTrigger, new BuffTemplate { RemoveOnAttackedBuffTrigger = true }),
        (BuffRemoveOn.AttackedEtc, new BuffTemplate { RemoveOnAttackedEtc = true }),
        (BuffRemoveOn.DamageSpellDot, new BuffTemplate { RemoveOnDamageSpellDot = true }),
        (BuffRemoveOn.DamageEtcDot, new BuffTemplate { RemoveOnDamageEtcDot = true }),
        (BuffRemoveOn.DamageBuffTrigger, new BuffTemplate { RemoveOnDamageBuffTrigger = true }),
        (BuffRemoveOn.DamageEtc, new BuffTemplate { RemoveOnDamageEtc = true }),
        (BuffRemoveOn.DamagedSpellDot, new BuffTemplate { RemoveOnDamagedSpellDot = true }),
        (BuffRemoveOn.DamagedEtcDot, new BuffTemplate { RemoveOnDamagedEtcDot = true }),
        (BuffRemoveOn.DamagedBuffTrigger, new BuffTemplate { RemoveOnDamagedBuffTrigger = true }),
        (BuffRemoveOn.DamagedEtc, new BuffTemplate { RemoveOnDamagedEtc = true }),
        (BuffRemoveOn.AutoAttack, new BuffTemplate { RemoveOnAutoAttack = true }),
        (BuffRemoveOn.Summoned, new BuffTemplate { RemoveBySummoned = true }),
        // One slot changed: bit 17 Ranged, the mask 감정 표현_궁수 15733/29615 carries.
        (BuffRemoveOn.ChangeEquipments, new BuffTemplate { RemoveOnChangeEquipments = 131072 })
    ];

    [Test]
    public async Task Matches_EachFlag_MatchesItsOwnColumnAndNoOther()
    {
        var mismatches = new List<string>();

        foreach (var (on, template) in FlagTemplates)
        {
            foreach (var (otherOn, _) in FlagTemplates)
            {
                // The two payload-carrying flags are tested separately: they need a value that fits.
                if (on is BuffRemoveOn.SourceDead or BuffRemoveOn.StartSkill ||
                    otherOn is BuffRemoveOn.SourceDead or BuffRemoveOn.StartSkill)
                    continue;

                var value = otherOn == BuffRemoveOn.Unmount ? 1u : otherOn == BuffRemoveOn.ChangeEquipments ? 17u : 0u;
                var expected = otherOn == on;

                if (BuffRemoveOnRules.Matches(otherOn, template, value, 0) != expected)
                    mismatches.Add($"flag {on} answered {!expected} to event {otherOn}");
            }
        }

        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task Matches_NoTemplate_MatchesNothing()
    {
        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.Death, null, 0, 0)).IsFalse();
    }

    [Test]
    public async Task Matches_SourceDead_OnlyTheDeadUnitsOwnInstance()
    {
        var template = new BuffTemplate { RemoveOnSourceDead = true };

        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.SourceDead, template, 7, 7)).IsTrue();
        // Another caster's instance of the same family is not the one the dead unit applied.
        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.SourceDead, template, 7, 8)).IsFalse();
        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.SourceDead, template, 0, 8)).IsFalse();
    }

    [Test]
    public async Task Matches_StartSkill_ExemptsAStartThatCarriesTheBuffsTag()
    {
        // Skill.Start passes the skill's cancel_ongoing_buff_exception_tag_id; a buff carrying that tag is
        // the one the skill meant to leave alone.
        var template = new BuffTemplate { RemoveOnStartSkill = true };

        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.StartSkill, template, 0, 0)).IsTrue();
        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.StartSkill, template, 4154, 0, _ => false)).IsTrue();
        await Assert.That(BuffRemoveOnRules.Matches(BuffRemoveOn.StartSkill, template, 4154, 0, tag => tag == 4154)).IsFalse();
    }

    [Test]
    public async Task UnmountMatches_NoPoint_KeepsTheOldAnySeatBehaviour()
    {
        // 130 of the 780 remove_on_unmount carriers name no seat.
        await Assert.That(BuffRemoveOnRules.UnmountMatches(0, 1)).IsTrue();
        await Assert.That(BuffRemoveOnRules.UnmountMatches(0, 0)).IsTrue();
    }

    [Test]
    public async Task UnmountMatches_ANamedSeat_OnlyThatSeat()
    {
        // 719 of the 771 buffs naming a seat name 1 Driver.
        await Assert.That(BuffRemoveOnRules.UnmountMatches(1, 1)).IsTrue();
        await Assert.That(BuffRemoveOnRules.UnmountMatches(1, 2)).IsFalse();
        // 23 name 80 Telescope, which UnbindSlave can hand over.
        await Assert.That(BuffRemoveOnRules.UnmountMatches(80, 80)).IsTrue();
        await Assert.That(BuffRemoveOnRules.UnmountMatches(80, 1)).IsFalse();
    }

    [Test]
    public async Task UnmountMatches_UnresolvedSeat_Removes()
    {
        // The seat is 0 None when the ship had no entry for the character; that is not evidence to keep
        // the buff, and it is what the code did before the column was read.
        await Assert.That(BuffRemoveOnRules.UnmountMatches(1, 0)).IsTrue();
        await Assert.That(BuffRemoveOnRules.UnmountMatches(80, 0)).IsTrue();
    }

    [Test]
    public async Task IsEquipmentChangeMasked_RangedBit_FiresOnTheRangedSlotOnly()
    {
        // 15733/29615 감정 표현_궁수: 131072 = bit 17.
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(131072, 17)).IsTrue();
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(131072, 16)).IsFalse();
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(131072, 18)).IsFalse();
    }

    [Test]
    public async Task IsEquipmentChangeMasked_MainhandBit_FiresOnMainhandOnly()
    {
        // 31644 테스트: 32768 = bit 15.
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(32768, 15)).IsTrue();
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(32768, 17)).IsFalse();
    }

    [Test]
    public async Task IsEquipmentChangeMasked_TheWideMask_IsEveryArmourWeaponAndCosplaySlot()
    {
        // 29238 잠재능력 발현: 134733823 covers 0-12, 14-18 and 27, and nothing else.
        const long mask = 134733823;
        var covered = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16, 17, 18, 27 };
        var uncovered = new[] { 13, 19, 20, 21, 22, 23, 24, 25, 26, 28, 63 };

        var mismatches = new List<string>();
        foreach (var slot in covered)
            if (!BuffRemoveOnRules.IsEquipmentChangeMasked(mask, (uint)slot))
                mismatches.Add($"slot {slot} should be in the mask");
        foreach (var slot in uncovered)
            if (BuffRemoveOnRules.IsEquipmentChangeMasked(mask, (uint)slot))
                mismatches.Add($"slot {slot} should not be in the mask");

        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task IsEquipmentChangeMasked_NoMaskOrOutOfRangeSlot_DoesNotFire()
    {
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(0, 15)).IsFalse();
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(1, 64)).IsFalse();
        await Assert.That(BuffRemoveOnRules.IsEquipmentChangeMasked(long.MinValue, 63)).IsTrue();
    }

    [Test]
    public async Task BreaksBuff_TheArrivingBuffsTag_NamesTheVictims()
    {
        // The shape of buff_breakers read by tag: tag 6 기절 removes the bard songs 656-660.
        var brokenByTag = new Dictionary<uint, IReadOnlyList<uint>>
        {
            [6] = [656, 657, 658, 659, 660]
        };

        await Assert.That(BuffRemoveOnRules.BreaksBuff([6], 656, tag => brokenByTag.GetValueOrDefault(tag))).IsTrue();
        await Assert.That(BuffRemoveOnRules.BreaksBuff([6], 2170, tag => brokenByTag.GetValueOrDefault(tag))).IsFalse();
    }

    [Test]
    public async Task BreaksBuff_NoTagsOrNoRows_DoNotBreak()
    {
        var brokenByTag = new Dictionary<uint, IReadOnlyList<uint>>
        {
            [6] = [656]
        };

        await Assert.That(BuffRemoveOnRules.BreaksBuff([], 656, tag => brokenByTag.GetValueOrDefault(tag))).IsFalse();
        await Assert.That(BuffRemoveOnRules.BreaksBuff([97], 656, tag => brokenByTag.GetValueOrDefault(tag))).IsFalse();
        await Assert.That(BuffRemoveOnRules.BreaksBuff(null, 656, tag => brokenByTag.GetValueOrDefault(tag))).IsFalse();
    }
}
