using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Procs;

public class ItemProcRulesTests
{
    /// <summary>The chance kinds item_procs populates in 10.0.2.13 (202 rows over 14 of the 20 enum values).</summary>
    private static readonly ProcChanceKind[] PopulatedKinds =
    [
        ProcChanceKind.HitAny, ProcChanceKind.HitMelee, ProcChanceKind.HitMeleeCrit, ProcChanceKind.HitSpell,
        ProcChanceKind.HitSpellCrit, ProcChanceKind.HitRanged, ProcChanceKind.HitRangedCrit,
        ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageMelee, ProcChanceKind.TakeDamageSpellCrit,
        ProcChanceKind.TakeDamageRanged, ProcChanceKind.HitHeal, ProcChanceKind.HitHealCrit, ProcChanceKind.FireSkill
    ];

    private static readonly DamageType[] AllDamageTypes =
        [DamageType.Melee, DamageType.Magic, DamageType.Siege, DamageType.Ranged, DamageType.Heal];

    private static IEnumerable<ProcChanceKind> EveryRaisedKind()
    {
        foreach (var damageType in AllDamageTypes)
        {
            foreach (var critical in new[] { false, true })
            {
                foreach (var kind in ItemProcRules.HitKinds(damageType, critical))
                    yield return kind;
                foreach (var kind in ItemProcRules.TakeDamageKinds(damageType, critical))
                    yield return kind;
            }
        }

        foreach (var kind in ItemProcRules.HealKinds(false))
            yield return kind;
        foreach (var kind in ItemProcRules.HealKinds(true))
            yield return kind;
        foreach (var kind in ItemProcRules.SkillFiredKinds())
            yield return kind;
    }

    [Test]
    public async Task HitKinds_EveryHitRaisesHitAny()
    {
        foreach (var damageType in AllDamageTypes)
        {
            await Assert.That(ItemProcRules.HitKinds(damageType, false)).Contains(ProcChanceKind.HitAny);
            await Assert.That(ItemProcRules.HitKinds(damageType, true)).Contains(ProcChanceKind.HitAny);
        }
    }

    [Test]
    public async Task HitKinds_MeleeHitRaisesMeleeButNotItsCritical()
    {
        var kinds = ItemProcRules.HitKinds(DamageType.Melee, false);

        await Assert.That(kinds).IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitMelee });
    }

    [Test]
    public async Task HitKinds_MeleeCriticalRaisesAnyMeleeAndMeleeCritical()
    {
        // A critical melee hit is still a melee hit: a proc on kind 2 rolls on it too.
        var kinds = ItemProcRules.HitKinds(DamageType.Melee, true);

        await Assert.That(kinds).IsEquivalentTo(
            new[] { ProcChanceKind.HitAny, ProcChanceKind.HitMelee, ProcChanceKind.HitMeleeCrit });
    }

    [Test]
    public async Task HitKinds_SpellFollowsItsOwnKinds()
    {
        await Assert.That(ItemProcRules.HitKinds(DamageType.Magic, false))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitSpell });
        await Assert.That(ItemProcRules.HitKinds(DamageType.Magic, true))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitSpell, ProcChanceKind.HitSpellCrit });
    }

    [Test]
    public async Task HitKinds_RangedFollowsItsOwnKinds()
    {
        await Assert.That(ItemProcRules.HitKinds(DamageType.Ranged, false))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitRanged });
        await Assert.That(ItemProcRules.HitKinds(DamageType.Ranged, true))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitRanged, ProcChanceKind.HitRangedCrit });
    }

    [Test]
    public async Task HitKinds_SiegeHasNoCriticalKind()
    {
        // enum_proc_chance_type has no siege critical, and DamageEffect never rolls one.
        await Assert.That(ItemProcRules.HitKinds(DamageType.Siege, false))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitSiege });
        await Assert.That(ItemProcRules.HitKinds(DamageType.Siege, true))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny, ProcChanceKind.HitSiege });
    }

    [Test]
    public async Task HitKinds_HealDamageTypeRaisesOnlyHitAny()
    {
        await Assert.That(ItemProcRules.HitKinds(DamageType.Heal, true))
            .IsEquivalentTo(new[] { ProcChanceKind.HitAny });
    }

    [Test]
    public async Task HitKinds_NeverRaisesAVictimHealOrFireKind()
    {
        foreach (var damageType in AllDamageTypes)
        {
            foreach (var kind in ItemProcRules.HitKinds(damageType, true))
                await Assert.That((int)kind).IsBetween(1, 8);
        }
    }

    [Test]
    public async Task TakeDamageKinds_MirrorsHitKindsEightHigher()
    {
        // 9-16 are the victim-side twins of 1-8, in the same order.
        foreach (var damageType in AllDamageTypes)
        {
            foreach (var critical in new[] { false, true })
            {
                var expected = ItemProcRules.HitKinds(damageType, critical).Select(k => (ProcChanceKind)((int)k + 8));

                await Assert.That(ItemProcRules.TakeDamageKinds(damageType, critical)).IsEquivalentTo(expected);
            }
        }
    }

    [Test]
    public async Task TakeDamageKinds_MeleeCriticalTaken()
    {
        await Assert.That(ItemProcRules.TakeDamageKinds(DamageType.Melee, true)).IsEquivalentTo(
            new[] { ProcChanceKind.TakeDamageAny, ProcChanceKind.TakeDamageMelee, ProcChanceKind.TakeDamageMeleeCrit });
    }

    [Test]
    public async Task HealKinds_AHealRaisesHitHealOnly()
    {
        await Assert.That(ItemProcRules.HealKinds(false)).IsEquivalentTo(new[] { ProcChanceKind.HitHeal });
    }

    [Test]
    public async Task HealKinds_ACriticalHealRaisesBoth()
    {
        await Assert.That(ItemProcRules.HealKinds(true))
            .IsEquivalentTo(new[] { ProcChanceKind.HitHeal, ProcChanceKind.HitHealCrit });
    }

    [Test]
    public async Task SkillFiredKinds_RaisesFireSkillOnly()
    {
        await Assert.That(ItemProcRules.SkillFiredKinds()).IsEquivalentTo(new[] { ProcChanceKind.FireSkill });
    }

    [Test]
    public async Task EveryPopulatedKind_IsRaisedBySomeEvent()
    {
        var raised = EveryRaisedKind().ToHashSet();

        foreach (var kind in PopulatedKinds)
            await Assert.That(raised).Contains(kind);
    }

    [Test]
    public async Task HitSkill_IsNeverRaised()
    {
        // Kind 20 has no row in 10.0.2.13; nothing routes to it.
        await Assert.That(EveryRaisedKind()).DoesNotContain(ProcChanceKind.HitSkill);
    }

    [Test]
    public async Task IsCritical_TheThreeCriticalsOnly()
    {
        await Assert.That(ItemProcRules.IsCritical(SkillHitType.MeleeCritical)).IsTrue();
        await Assert.That(ItemProcRules.IsCritical(SkillHitType.SpellCritical)).IsTrue();
        await Assert.That(ItemProcRules.IsCritical(SkillHitType.RangedCritical)).IsTrue();

        await Assert.That(ItemProcRules.IsCritical(SkillHitType.MeleeHit)).IsFalse();
        await Assert.That(ItemProcRules.IsCritical(SkillHitType.SpellHit)).IsFalse();
        await Assert.That(ItemProcRules.IsCritical(SkillHitType.RangedHit)).IsFalse();
        await Assert.That(ItemProcRules.IsCritical(SkillHitType.Invalid)).IsFalse();
    }

    [Test]
    public async Task TriggerMatches_NoFilterAnswersAnySkill()
    {
        // 186 of the 202 rows set neither trigger column.
        await Assert.That(ItemProcRules.TriggerMatches(0, 0, 2, false)).IsTrue();
        await Assert.That(ItemProcRules.TriggerMatches(0, 0, 0, false)).IsTrue();
    }

    [Test]
    public async Task TriggerMatches_SkillFilterNeedsThatSkill()
    {
        // Proc 109 answers 11943 (불협 화음) and nothing else.
        await Assert.That(ItemProcRules.TriggerMatches(11943, 0, 11943, false)).IsTrue();
        await Assert.That(ItemProcRules.TriggerMatches(11943, 0, 11934, false)).IsFalse();
        await Assert.That(ItemProcRules.TriggerMatches(11943, 0, 0, false)).IsFalse();
    }

    [Test]
    public async Task TriggerMatches_TagFilterNeedsTheTag()
    {
        // Procs 174-179 answer any skill carrying tag 378.
        await Assert.That(ItemProcRules.TriggerMatches(0, 378, 10082, true)).IsTrue();
        await Assert.That(ItemProcRules.TriggerMatches(0, 378, 2, false)).IsFalse();
    }

    [Test]
    public async Task TriggerMatches_BothFiltersNeedBoth()
    {
        // No shipped row sets both; one that did would ask for both.
        await Assert.That(ItemProcRules.TriggerMatches(11943, 378, 11943, true)).IsTrue();
        await Assert.That(ItemProcRules.TriggerMatches(11943, 378, 11943, false)).IsFalse();
        await Assert.That(ItemProcRules.TriggerMatches(11943, 378, 10082, true)).IsFalse();
    }

    [Test]
    public async Task FinisherAllows_PlainRowIgnoresTheKill()
    {
        await Assert.That(ItemProcRules.FinisherAllows(false, false)).IsTrue();
        await Assert.That(ItemProcRules.FinisherAllows(false, true)).IsTrue();
    }

    [Test]
    public async Task FinisherAllows_FinisherRowNeedsTheKill()
    {
        // Proc 46, "적을 죽일 때 마다": each time you kill an enemy.
        await Assert.That(ItemProcRules.FinisherAllows(true, true)).IsTrue();
        await Assert.That(ItemProcRules.FinisherAllows(true, false)).IsFalse();
    }

    [Test]
    public async Task CooldownBlocks_ZeroCooldownNeverBlocks()
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(ItemProcRules.CooldownBlocks(now, 0, now)).IsFalse();
    }

    [Test]
    public async Task CooldownBlocks_InsideTheWindow()
    {
        var fired = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(ItemProcRules.CooldownBlocks(fired, 60, fired.AddSeconds(59))).IsTrue();
        await Assert.That(ItemProcRules.CooldownBlocks(fired, 60, fired)).IsTrue();
    }

    [Test]
    public async Task CooldownBlocks_ClearsAtTheWindowEnd()
    {
        var fired = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(ItemProcRules.CooldownBlocks(fired, 60, fired.AddSeconds(60))).IsFalse();
        await Assert.That(ItemProcRules.CooldownBlocks(fired, 180, fired.AddSeconds(181))).IsFalse();
    }

    [Test]
    public async Task CooldownBlocks_AProcThatNeverFiredIsReady()
    {
        await Assert.That(ItemProcRules.CooldownBlocks(DateTime.MinValue, 180, DateTime.UtcNow)).IsFalse();
    }

    [Test]
    public async Task SkillCooldownBlocks_RefusesAProcWhileItsSkillIsCoolingDown()
    {
        // Procs 116-119, 121, 122, 124 and 125 cast with bypassGcd, which in Skill.Use also skips the skill's own
        // cooldown check while the cast still arms it, so Apply refuses on the skill's cooldown as well.
        await Assert.That(ItemProcRules.SkillCooldownBlocks(true)).IsTrue();
        await Assert.That(ItemProcRules.SkillCooldownBlocks(false)).IsFalse();
    }

    [Test]
    public async Task SkillCooldownBlocks_IsASeparateGateFromTheProcCooldown()
    {
        // Proc 118: cooldown_sec 0, so its own window never blocks, while skill 31317 sits on 60 s.
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(ItemProcRules.CooldownBlocks(now, 0, now.AddSeconds(1))).IsFalse();
        await Assert.That(ItemProcRules.SkillCooldownBlocks(true)).IsTrue();
    }

    [Test]
    public async Task RollPasses_RateZeroNeverPasses()
    {
        for (var roll = 0; roll < 100; roll++)
            await Assert.That(ItemProcRules.RollPasses(0, roll)).IsFalse();
    }

    [Test]
    public async Task RollPasses_RateHundredAlwaysPasses()
    {
        for (var roll = 0; roll < 100; roll++)
            await Assert.That(ItemProcRules.RollPasses(100, roll)).IsTrue();
    }

    [Test]
    public async Task RollPasses_FifteenPercentPassesFifteenDrawsInAHundred()
    {
        var passed = Enumerable.Range(0, 100).Count(roll => ItemProcRules.RollPasses(15, roll));

        await Assert.That(passed).IsEqualTo(15);
    }

    [Test]
    public async Task TargetSide_SelfIsTheOwner()
    {
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Self)).IsEqualTo(ItemProcTargetSide.Owner);
    }

    [Test]
    public async Task TargetSide_UnitTargetsAreTheOtherSide()
    {
        // The target types the 40 non-self proc skills use, and the unit types alongside them.
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Friendly)).IsEqualTo(ItemProcTargetSide.Other);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Hostile)).IsEqualTo(ItemProcTargetSide.Other);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.AnyUnit)).IsEqualTo(ItemProcTargetSide.Other);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.AnyUnitAlways)).IsEqualTo(ItemProcTargetSide.Other);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Others)).IsEqualTo(ItemProcTargetSide.Other);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.FriendlyOthers)).IsEqualTo(ItemProcTargetSide.Other);
    }

    [Test]
    public async Task TargetSide_PlacementItemAndDoodadTargetsAreUnsupported()
    {
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Pos)).IsEqualTo(ItemProcTargetSide.Unsupported);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Item)).IsEqualTo(ItemProcTargetSide.Unsupported);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Doodad)).IsEqualTo(ItemProcTargetSide.Unsupported);
        await Assert.That(ItemProcRules.TargetSide(SkillTargetType.Line)).IsEqualTo(ItemProcTargetSide.Unsupported);
    }

    [Test]
    public async Task RequirementTargetIsOwner_TheTakeDamageKindsOnly()
    {
        // Procs 87, 114 and 153 are take_damage_any with a self-target skill: their kind 26 band is the wearer's.
        foreach (var kind in Enum.GetValues<ProcChanceKind>())
        {
            var takeDamage = (int)kind is >= 9 and <= 16;

            await Assert.That(ItemProcRules.RequirementTargetIsOwner(kind)).IsEqualTo(takeDamage);
        }
    }

    [Test]
    public async Task RequirementTargetIsOwner_HitAndHealReadTheOtherSide()
    {
        // Procs 173 and 198 are hit_heal rows whose band is the health of the unit the wearer healed.
        await Assert.That(ItemProcRules.RequirementTargetIsOwner(ProcChanceKind.HitHeal)).IsFalse();
        await Assert.That(ItemProcRules.RequirementTargetIsOwner(ProcChanceKind.HitHealCrit)).IsFalse();
        await Assert.That(ItemProcRules.RequirementTargetIsOwner(ProcChanceKind.HitAny)).IsFalse();
        await Assert.That(ItemProcRules.RequirementTargetIsOwner(ProcChanceKind.FireSkill)).IsFalse();
    }

    [Test]
    public async Task StartsCooldown_OnlyASuccessfulCast()
    {
        await Assert.That(ItemProcRules.StartsCooldown(SkillResult.Success)).IsTrue();

        await Assert.That(ItemProcRules.StartsCooldown(SkillResult.NoTarget)).IsFalse();
        await Assert.That(ItemProcRules.StartsCooldown(SkillResult.InvalidTarget)).IsFalse();
        await Assert.That(ItemProcRules.StartsCooldown(SkillResult.CooldownTime)).IsFalse();
        await Assert.That(ItemProcRules.StartsCooldown(SkillResult.LackMana)).IsFalse();
        await Assert.That(ItemProcRules.StartsCooldown(SkillResult.TooFarRange)).IsFalse();
    }

    [Test]
    public async Task SourceMayProc_ProcSkillsDoNotChain()
    {
        await Assert.That(ItemProcRules.SourceMayProc(false)).IsTrue();
        await Assert.That(ItemProcRules.SourceMayProc(true)).IsFalse();
    }
}
