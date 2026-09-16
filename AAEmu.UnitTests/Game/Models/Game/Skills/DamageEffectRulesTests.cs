using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class DamageEffectRulesTests
{
    private const int VictimMaxHealth = 10_000;
    private const int CasterMaxHealth = 20_000;

    // ---------------------------------------------------------------------------------------------------
    // percent damage
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task PercentDamageTerm_TenPercentOfTheVictimsMaximumHealth_IsTenPercent()
    {
        // The shape the acceptance test asks for: percent_min = percent_max = 10 on a 10,000 HP victim.
        var term = DamageEffectRules.PercentDamageTerm(10, 10, roll: 0.37f, VictimMaxHealth);

        await Assert.That(term).IsEqualTo(1000f);
    }

    [Test]
    public async Task PercentDamageTerm_ShippedBossBand_IsTheShareOfTheVictimsMaximumHealth()
    {
        // damage_effects 5001 (다후타의 해일): 150..200 % of the victim's maximum health, siege, no weapon.
        // A full roll is the instant kill the skill's own text describes ("한 번에 죽일만한 공성피해").
        var term = DamageEffectRules.PercentDamageTerm(150, 200, roll: 1f, poolValue: 12_000);

        await Assert.That(term).IsEqualTo(24_000f);
    }

    [Test]
    public async Task PercentDamageTerm_SourcePool_ReadsTheCastersHealth()
    {
        // Skill 44265 방패 휘두르기: 바위 — "자신의 최대 생명력 1%~2%만큼의 추가 피해": use_source_health is
        // set, so the 2 % comes off the caster's 20,000 maximum health (400), not the victim's (200).
        var fromCaster = DamageEffectRules.PercentDamageTerm(2, 2, roll: 0f, CasterMaxHealth);
        var fromVictim = DamageEffectRules.PercentDamageTerm(2, 2, roll: 0f, VictimMaxHealth);

        await Assert.That(fromCaster).IsEqualTo(400f);
        await Assert.That(fromVictim).IsEqualTo(200f);
    }

    [Test]
    public async Task PercentDamageTerm_CurrentHealth_ReadsTheHealthLeft()
    {
        // damage_effects 7609 (자폭하기): 80..90 % of the *current* health, type 1, so a half-dead unit adds
        // less than a fresh one.
        var half = DamageEffectRules.PercentDamageTerm(85, 85, 0f, poolValue: 5_000);
        var full = DamageEffectRules.PercentDamageTerm(85, 85, 0f, poolValue: 10_000);

        await Assert.That(half).IsEqualTo(4250f);
        await Assert.That(full).IsEqualTo(8500f);
    }

    [Test]
    public async Task PercentDamageTerm_ZeroBand_OrAnEmptyPool_AddsNothing()
    {
        // 0..0 is what a row that never rolled a percentage ships; an empty pool (a dead unit's health, a
        // unit with no mana) has nothing to take a share of.
        await Assert.That(DamageEffectRules.PercentDamageTerm(0, 0, 0.9f, VictimMaxHealth))
            .IsEqualTo(DamageEffectRules.NeutralTerm);
        await Assert.That(DamageEffectRules.PercentDamageTerm(10, 10, 0.5f, poolValue: 0))
            .IsEqualTo(DamageEffectRules.NeutralTerm);
    }

    [Test]
    public async Task PercentRoll_SpansTheBandInclusively()
    {
        await Assert.That(DamageEffectRules.PercentRoll(30, 40, 0f)).IsEqualTo(30f);
        await Assert.That(DamageEffectRules.PercentRoll(30, 40, 0.5f)).IsEqualTo(35f);
        await Assert.That(DamageEffectRules.PercentRoll(30, 40, 1f)).IsEqualTo(40f);
    }

    [Test]
    public async Task PercentRoll_ClampsTheRollAndSurvivesAnInvertedBand()
    {
        await Assert.That(DamageEffectRules.PercentRoll(30, 40, 1.5f)).IsEqualTo(40f);
        await Assert.That(DamageEffectRules.PercentRoll(30, 40, -1f)).IsEqualTo(30f);
        // No shipped row inverts its band; reading it as its lower bound keeps the term sane if one appears.
        await Assert.That(DamageEffectRules.PercentRoll(40, 30, 0.5f)).IsEqualTo(40f);
    }

    [Test]
    public async Task UnitResourcePools_ReadsThePoolTheEnumNames()
    {
        var unit = new AAEmu.Game.Models.Game.Units.Unit
        {
            Hp = 111,
            MaxHp = 222,
            Mp = 333,
            MaxMp = 444
        };

        await Assert.That(UnitResourcePools.ValueOf(unit, PercentDamageResourceType.CurrentHealth)).IsEqualTo(111);
        await Assert.That(UnitResourcePools.ValueOf(unit, PercentDamageResourceType.MaxHealth)).IsEqualTo(222);
        await Assert.That(UnitResourcePools.ValueOf(unit, PercentDamageResourceType.CurrentMana)).IsEqualTo(333);
        await Assert.That(UnitResourcePools.ValueOf(unit, PercentDamageResourceType.MaxMana)).IsEqualTo(444);
        // An id outside the five rows of enum_percent_damage_resource_types has no pool behind it.
        await Assert.That(UnitResourcePools.ValueOf(unit, (PercentDamageResourceType)9)).IsEqualTo(0);
    }

    // ---------------------------------------------------------------------------------------------------
    // target health scaling
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task TargetHealthAdjust_WithoutABand_ReturnsTheHitUnchanged()
    {
        // 10,998 of the 11,001 rows: target_health_max 0. Every one of them must hand the rolled hit back
        // untouched — for the 26 rows with target_health_mul 0 that is the difference between a countdown
        // and no damage at all.
        var rolled = 137.5f;

        var adjusted = DamageEffectRules.TargetHealthAdjust(
            rolled, victimHealthPercent: 50, minPercent: 0, maxPercent: 0, mul: 0f, add: 0);

        await Assert.That(adjusted).IsEqualTo(rolled);
    }

    [Test]
    public async Task TargetHealthAdjust_OutsideTheBand_ReturnsTheHitUnchanged()
    {
        // Effect 6094's band: 0..10 % of the victim's health.
        var rolled = 15000f;

        await Assert.That(DamageEffectRules.TargetHealthAdjust(
            rolled, victimHealthPercent: 11, minPercent: 0, maxPercent: 10, mul: 1f, add: 0)).IsEqualTo(rolled);
        await Assert.That(DamageEffectRules.TargetHealthAdjust(
            rolled, victimHealthPercent: 0, minPercent: 5, maxPercent: 10, mul: 1f, add: 0)).IsEqualTo(rolled);
    }

    [Test]
    public async Task TargetHealthAdjust_InsideTheBand_AppliesTheMulThenTheAdd()
    {
        await Assert.That(DamageEffectRules.TargetHealthAdjust(
            1000f, victimHealthPercent: 8, minPercent: 0, maxPercent: 10, mul: 2f, add: 50)).IsEqualTo(2050f);
        // The band is inclusive at both ends.
        await Assert.That(DamageEffectRules.TargetHealthAdjust(
            1000f, victimHealthPercent: 10, minPercent: 0, maxPercent: 10, mul: 3f, add: 0)).IsEqualTo(3000f);
        await Assert.That(DamageEffectRules.TargetHealthAdjust(
            1000f, victimHealthPercent: 0, minPercent: 0, maxPercent: 10, mul: 0.5f, add: 0)).IsEqualTo(500f);
    }

    [Test]
    public async Task TargetHealthAdjust_ShippedIdentityRows_AreExact()
    {
        // The three rows that ship a band (6094, 6103, 16145) author mul 1.0 and add 0, so an in-band hit is
        // the hit itself — 10.0.2.13 authors no row where this scale does anything.
        var rolled = 12345.678f;

        var adjusted = DamageEffectRules.TargetHealthAdjust(
            rolled, victimHealthPercent: 5, minPercent: 0, maxPercent: 10, mul: 1f, add: 0);

        await Assert.That(adjusted).IsEqualTo(rolled);
    }

    [Test]
    public async Task TargetHealthAdjust_InvertedBand_IsNoBand()
    {
        var rolled = 42f;

        await Assert.That(DamageEffectRules.TargetHealthAdjust(
            rolled, victimHealthPercent: 7, minPercent: 10, maxPercent: 3, mul: 0f, add: 0)).IsEqualTo(rolled);
    }
}
