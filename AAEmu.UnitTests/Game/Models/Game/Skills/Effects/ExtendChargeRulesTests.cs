using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="ExtendChargeRules"/>: the composition behind <see cref="ExtendChargeEffect"/>, the effect that
/// was a no-op TODO and left its 23 rows — 14 shields across skills 10153 보호막, 39887 개인 보호막,
/// 42784 정원의 가호, 43207 모두 치유: 파도 … — doing nothing at all.
/// </summary>
[NotInParallel]
public class ExtendChargeRulesTests
{
    private SingletonScope<SkillManager> _skills;

    [Before(Test)]
    public void InstallContentLookups() =>
        _skills = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());

    [After(Test)]
    public void RestoreContentLookups() => _skills.Dispose();

    [Test]
    public async Task TotalCharge_ADisabledSourceAddsNothing()
    {
        // The pin: every use_* flag off is an effect that adds nothing to the buff it names.
        var total = ExtendChargeRules.TotalCharge(
            useFixedCharge: false, fixedRoll: 5000,
            usePercentCharge: false, percentCharge: 1500,
            useLevelCharge: false, levelCharge: 1000f,
            useDpsCharge: false, dpsCharge: 2500f);

        await Assert.That(total).IsEqualTo(0);
    }

    [Test]
    public async Task TotalCharge_SumsTheSourcesTheRowEnables()
    {
        // extend-charge effect 1 enables percent + level + dps and not fixed.
        var total = ExtendChargeRules.TotalCharge(
            useFixedCharge: false, fixedRoll: 5000,
            usePercentCharge: true, percentCharge: 1500,
            useLevelCharge: true, levelCharge: 100f,
            useDpsCharge: true, dpsCharge: 250f);

        await Assert.That(total).IsEqualTo(1850);
    }

    [Test]
    public async Task TotalCharge_AFixedRowAddsTheFixedRollAlone()
    {
        // extend-charge effect 17 authors fixed 5000-5000 alongside a dps source it does not enable.
        var total = ExtendChargeRules.TotalCharge(
            useFixedCharge: true, fixedRoll: 5000,
            usePercentCharge: false, percentCharge: 0,
            useLevelCharge: false, levelCharge: 0f,
            useDpsCharge: false, dpsCharge: 0f);

        await Assert.That(total).IsEqualTo(5000);
    }

    [Test]
    public async Task PercentCharge_TakesTheShareOfTheNamedPool()
    {
        // extend-charge effect 1: 5 % of max_mana.
        var charge = ExtendChargeRules.PercentCharge(
            ExtendChargeRules.ChargeResource.MaxMana, 5, pool: 30_000);

        await Assert.That(charge).IsEqualTo(1500);
    }

    [Test]
    public async Task PercentCharge_MaxHealthRowsReadHealth()
    {
        // extend-charge effect 8: percent_max 100 against max_health — the 100 % rows are a full bar.
        var charge = ExtendChargeRules.PercentCharge(
            ExtendChargeRules.ChargeResource.MaxHealth, 100, pool: 12_345);

        await Assert.That(charge).IsEqualTo(12_345);
    }

    [Test]
    public async Task PercentCharge_WithoutAPool_AddsNothing()
    {
        // percent_damage_resource_type_id 0 is "no pool authored", not "health".
        var charge = ExtendChargeRules.PercentCharge(
            ExtendChargeRules.ChargeResource.None, 100, pool: 12_345);

        await Assert.That(charge).IsEqualTo(0);
    }

    [Test]
    public async Task PercentCharge_AZeroPercentAddsNothing()
    {
        var charge = ExtendChargeRules.PercentCharge(
            ExtendChargeRules.ChargeResource.MaxMana, 0, pool: 30_000);

        await Assert.That(charge).IsEqualTo(0);
    }

    [Test]
    public async Task LevelCharge_WithoutTheColumn_IsExactlyZero()
    {
        // No authored level_md means no level term, not a term of the level rating.
        await Assert.That(ExtendChargeRules.LevelCharge(0f, 1234f, 50, 1, 1)).IsEqualTo(0f);
    }

    [Test]
    public async Task LevelCharge_ShippedRowReadsLevelMdTimesTheRating()
    {
        // extend-charge effect 30 authors level_md 1.05 with level_va_start == level_va_end == 1, so the band
        // contributes its 1 % flat and the term is 1.01 x 1.05 x level rating, +0.5 the way the damage and
        // heal effects round.
        var charge = ExtendChargeRules.LevelCharge(1.05f, 1000f, skillLevel: 50, levelVaStart: 1, levelVaEnd: 1);

        await Assert.That(charge).IsEqualTo(1061f);
    }

    [Test]
    public async Task LevelCharge_ABandScalesBetweenItsEnds()
    {
        // level_va_start 10 / level_va_end 1 (extend-charge effect 11 is 5/1) at skill level 1: the band
        // modifier is (0/49 * (1 - 10) + 10) * 0.01 = 0.10, so the term is 1.10 x the 1,000 base.
        var charge = ExtendChargeRules.LevelCharge(10f, 100f, skillLevel: 1, levelVaStart: 10, levelVaEnd: 1);

        await Assert.That(charge).IsEqualTo(1100.5f);
    }

    [Test]
    public async Task DpsCharge_WithoutTheColumn_AddsNoMeaningfulCharge()
    {
        // dps_inc_multiplier 0 (all 23 rows author 1.0 or more) leaves the stat term at a thousandth of the
        // rating, which truncates away in TotalCharge's int.
        var charge = ExtendChargeRules.DpsCharge(0f, dpsInc: 5000, dpsMultiplier: 0f, weaponDps: 0f);

        await Assert.That(charge).IsLessThan(0.1f);
    }

    [Test]
    public async Task DpsCharge_ShippedRowReadsTheStat()
    {
        // extend-charge effect 1: dps_inc_multiplier 1.5 over the melee dps_inc stat of 5000.
        var charge = ExtendChargeRules.DpsCharge(1.5f, dpsInc: 5000, dpsMultiplier: 0f, weaponDps: 0f);

        await Assert.That(charge).IsEqualTo(7.5f);
    }

    [Test]
    public async Task DpsCharge_NoWeaponFlag_IgnoresTheWeapon()
    {
        // All three weapon flags are false on all 23 rows, so the caller passes dpsMultiplier 0 and the
        // weapon never enters the shipped term.
        var charge = ExtendChargeRules.DpsCharge(1f, dpsInc: 1000, dpsMultiplier: 0f, weaponDps: 9999f);

        await Assert.That(charge).IsEqualTo(1f);
    }

    [Test]
    public async Task ExtendedCharge_AddsToWhatIsLeftOnTheShield()
    {
        // Buff 95 보호막 (skill 10153) has max_charge 0 = no ceiling authored.
        await Assert.That(ExtendChargeRules.ExtendedCharge(liveCharge: 4000, added: 1500, maxCharge: 0))
            .IsEqualTo(5500);
    }

    [Test]
    public async Task ExtendedCharge_HoldsAtTheAuthoredCeiling()
    {
        // Buff 22574 보호막 (extend-charge effect 13) authors max_charge 20,000.
        await Assert.That(ExtendChargeRules.ExtendedCharge(liveCharge: 19_500, added: 1500, maxCharge: 20_000))
            .IsEqualTo(20_000);
    }

    [Test]
    public async Task ExtendedCharge_NegativeLiveChargeIsTreatedAsSpent()
    {
        await Assert.That(ExtendChargeRules.ExtendedCharge(liveCharge: -5, added: 1500, maxCharge: 0))
            .IsEqualTo(1500);
    }
}
