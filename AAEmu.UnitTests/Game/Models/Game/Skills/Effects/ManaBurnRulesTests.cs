using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="ManaBurnRules"/>: the sources a mana-burn row enables, and the share of the burn that lands
/// as health damage.
/// </summary>
public class ManaBurnRulesTests
{
    [Test]
    public async Task LevelCharge_WithoutTheColumn_IsExactlyZero()
    {
        // The 24 rows that ship use_level_charge false all author level_md 1.0 (the table default), so the
        // old unconditional term burned a full level rating they never asked for.
        var charge = ManaBurnRules.LevelCharge(
            useLevelCharge: false, levelMd: 1.0f, levelDps: 1234f, skillLevel: 50, levelVaStart: 0, levelVaEnd: 0);

        await Assert.That(charge).IsEqualTo(0f);
    }

    [Test]
    public async Task LevelCharge_WithTheColumn_ReadsTheLevelRating()
    {
        // mana-burn effect 93 authors level_md 2.0 with a 1/1 band (the 76 rows that enable it are mostly 1/1).
        var charge = ManaBurnRules.LevelCharge(
            useLevelCharge: true, levelMd: 2.0f, levelDps: 1000f, skillLevel: 50, levelVaStart: 1, levelVaEnd: 1);

        await Assert.That(charge).IsEqualTo(2020.5f);
    }

    [Test]
    public async Task FixedCharge_WithNeitherTheFlagNorAPair_AddsNothing()
    {
        // The 30 rows that ship use_fixed_charge false all author 0/0.
        await Assert.That(ManaBurnRules.FixedCharge(useFixedCharge: false, baseMin: 0, baseMax: 0, roll: 0))
            .IsEqualTo(0);
    }

    [Test]
    public async Task FixedCharge_WithTheColumn_ReadsTheBasePair()
    {
        await Assert.That(ManaBurnRules.FixedCharge(useFixedCharge: true, baseMin: 1000, baseMax: 1000, roll: 0))
            .IsEqualTo(1000);
        await Assert.That(ManaBurnRules.FixedCharge(useFixedCharge: true, baseMin: 100, baseMax: 200, roll: 0))
            .IsEqualTo(100);
        await Assert.That(ManaBurnRules.FixedCharge(useFixedCharge: true, baseMin: 100, baseMax: 200, roll: 100))
            .IsEqualTo(200);
    }

    [Test]
    public async Task FixedCharge_WithAnAuthoredPairAndNoFlag_StillReadsIt()
    {
        // An authored base is itself the statement "this row burns a fixed charge" - the flag is what a row
        // that carries neither uses. No shipped row has one without the other.
        await Assert.That(ManaBurnRules.FixedCharge(useFixedCharge: false, baseMin: 100, baseMax: 200, roll: 0))
            .IsEqualTo(100);
    }

    [Test]
    public async Task PercentCharge_ReadsThePoolTheRowNames()
    {
        // mana-burn effect 91: 50 % of the victim's current mana (resource type 1).
        var charge = ManaBurnRules.PercentCharge(
            ManaBurnRules.ChargeResource.CurrentMana, percent: 50, pool: 8000);

        await Assert.That(charge).IsEqualTo(4000);
    }

    [Test]
    public async Task PercentCharge_WithoutAPool_AddsNothing()
    {
        await Assert.That(ManaBurnRules.PercentCharge(
            ManaBurnRules.ChargeResource.None, percent: 100, pool: 8000)).IsEqualTo(0);
    }

    [Test]
    public async Task TotalCharge_IsTheSumOfTheEnabledSources()
    {
        await Assert.That(ManaBurnRules.TotalCharge(fixedCharge: 1000, percentCharge: 4000, levelCharge: 20.5f))
            .IsEqualTo(5020);
    }

    [Test]
    public async Task TotalCharge_WithNothingEnabled_IsZero()
    {
        // mana-burn effect 131 is the one row with all three off; it burns nothing rather than its base.
        await Assert.That(ManaBurnRules.TotalCharge(0, 0, 0f)).IsEqualTo(0);
    }

    [Test]
    public async Task HealthDamage_IsTheRatioPerTenThousand()
    {
        // 10000 on 28 rows is a full 100 %, 500 on 9 is 5 %, 15000 on one is 150 %.
        await Assert.That(ManaBurnRules.HealthDamage(burnedMana: 1000, damageRatio: 10000)).IsEqualTo(1000);
        await Assert.That(ManaBurnRules.HealthDamage(burnedMana: 1000, damageRatio: 500)).IsEqualTo(50);
        await Assert.That(ManaBurnRules.HealthDamage(burnedMana: 1000, damageRatio: 15000)).IsEqualTo(1500);
    }

    [Test]
    public async Task HealthDamage_WithARatioOfZero_IsExactlyZero()
    {
        // 62 of the 100 rows: mana burn that never touches health, which is what the effect did before.
        await Assert.That(ManaBurnRules.HealthDamage(burnedMana: 1000, damageRatio: 0)).IsEqualTo(0);
        await Assert.That(ManaBurnRules.HealthDamage(burnedMana: 0, damageRatio: 10000)).IsEqualTo(0);
    }
}
