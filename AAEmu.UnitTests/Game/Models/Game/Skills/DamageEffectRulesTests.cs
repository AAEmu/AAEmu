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
    // aggro and procs
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task AggroValue_TheDefaultMultiplier_IsTheDamageItself()
    {
        // 10,837 of the 11,001 rows sit at the 1.0 default, and that has to be the aggro they already had.
        foreach (var damage in new[] { 0, 1, 37, 100, 9_999, 1_000_000 })
        {
            await Assert.That(DamageEffectRules.AggroValue(damage, 1f)).IsEqualTo(damage);
        }
    }

    [Test]
    public async Task AggroValue_ScalesTheThreatTheRowsAuthor()
    {
        // 방패 휘두르기 (damage effects 11583/12248, "위협수준 생성량 높음") and 3단 베기/진공 폭발.
        await Assert.That(DamageEffectRules.AggroValue(100, 10f)).IsEqualTo(1000);
        await Assert.That(DamageEffectRules.AggroValue(100, 3f)).IsEqualTo(300);
        await Assert.That(DamageEffectRules.AggroValue(100, 1.5f)).IsEqualTo(150);
        // The 77 rows that pull nothing (핏물먹이의 돌개바람, 극한의 얼음, 오스트 마력탑의 소환물 흡수).
        await Assert.That(DamageEffectRules.AggroValue(100, 0f)).IsEqualTo(0);
        await Assert.That(DamageEffectRules.AggroValue(100, 0.01f)).IsEqualTo(1);
    }

    [Test]
    public async Task FiresProcs_FollowsTheFlag()
    {
        // 't' on 10,760 rows (the roll stays put); the 241 that clear it are 감아올리기 and the rest of the
        // grapple family.
        await Assert.That(DamageEffectRules.FiresProcs(true)).IsTrue();
        await Assert.That(DamageEffectRules.FiresProcs(false)).IsFalse();
    }

    // ---------------------------------------------------------------------------------------------------
    // critical bonus and the target buff tag pair
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task CriticalFactor_WithoutAnEffectBonus_IsTheExpressionItReplaced()
    {
        // 10,999 of the 11,001 rows leave critical_bonus at 0, and that has to crit for exactly what it
        // crit for before: 1 + (bonus - flexibility/100)/100.
        foreach (var unitBonus in new[] { 0f, 12.5f, 50f, 120f })
        {
            foreach (var flexibility in new[] { 0f, 100f, 350f })
            {
                var before = 1 + (unitBonus - flexibility / 100) / 100;
                await Assert.That(DamageEffectRules.CriticalFactor(unitBonus, 0, flexibility)).IsEqualTo(before);
            }
        }
    }

    [Test]
    public async Task CriticalFactor_AddsTheEffectsOwnBonus()
    {
        // damage_effects 2225 and 2227 author 100: a 50 % caster bonus becomes 150 %, and a bare caster
        // crits for double instead of for nothing.
        await Assert.That(DamageEffectRules.CriticalFactor(50f, 100, 0f)).IsEqualTo(2.5f);
        await Assert.That(DamageEffectRules.CriticalFactor(0f, 100, 0f)).IsEqualTo(2f);
    }

    [Test]
    public async Task TargetBuffDamage_WithoutAnAdd_IsTheMultiplierAlone()
    {
        // 10,980 of the 11,001 rows. `x * 1.0f + 0` is bit-for-bit `x * 1.0f`, which is what the code did.
        var damage = 12_345.678f;

        await Assert.That(DamageEffectRules.TargetBuffDamage(damage, 1f, 0)).IsEqualTo(damage);
        await Assert.That(DamageEffectRules.TargetBuffDamage(damage, 1.3f, 0)).IsEqualTo(damage * 1.3f);
    }

    [Test]
    public async Task TargetBuffDamage_AddsTheFlatDeltaTheTagRowsAuthor()
    {
        // 광선포 발사 against the armored rhino (tag 5803): 15,000 authored, 5,000 off. 죽음의 바다
        // against a swimmer (tag 4363): 5,000 authored, 53,000 on.
        await Assert.That(DamageEffectRules.TargetBuffDamage(15_000f, 1f, -5_000)).IsEqualTo(10_000f);
        await Assert.That(DamageEffectRules.TargetBuffDamage(5_000f, 1f, 53_000)).IsEqualTo(58_000f);
        // The multiplier still lands first: 죽음의 바다's 500-damage row doubles and then adds 10,000.
        await Assert.That(DamageEffectRules.TargetBuffDamage(500f, 2f, 10_000)).IsEqualTo(11_000f);
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
