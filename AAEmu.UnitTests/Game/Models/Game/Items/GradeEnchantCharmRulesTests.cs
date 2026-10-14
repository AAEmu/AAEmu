using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class GradeEnchantCharmRulesTests
{
    private static ItemGradeEnchantingSupport Charm(
        uint restrict = 0, uint exclusive = 0, int scaleMin = 31, int scaleMax = 31) => new()
    {
        RestrictItemTagId = restrict,
        ExclusiveItemTagId = exclusive,
        ReqScaleMinId = scaleMin,
        ReqScaleMaxId = scaleMax
    };

    [Test]
    public async Task ThirtyOneOnBothScaleColumns_IsNoScaleGate()
    {
        await Assert.That(GradeEnchantCharmRules.HasScaleRequirement(Charm())).IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(Charm(), false, false, 0)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(Charm(), false, false, 30)).IsTrue();
    }

    [Test]
    public async Task RestrictTag_BlacklistsTaggedGear()
    {
        var charm = Charm(restrict: 6059);

        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, itemHasRestrictTag: true, false, 0))
            .IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, itemHasRestrictTag: false, false, 0))
            .IsTrue();
    }

    [Test]
    public async Task ExclusiveTag_RequiresTheTagOnTheGear()
    {
        var charm = Charm(exclusive: 100);

        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, itemHasExclusiveTag: false, 0))
            .IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, itemHasExclusiveTag: true, 0))
            .IsTrue();
    }

    [Test]
    public async Task ScaleRange_MustContainTheItemsTemperingStep()
    {
        var charm = Charm(scaleMin: 18, scaleMax: 30);

        await Assert.That(GradeEnchantCharmRules.HasScaleRequirement(charm)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, false, 17)).IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, false, 18)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, false, 30)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, false, 31)).IsFalse();
    }

    [Test]
    public async Task ScaleRange_AcceptsEitherColumnOrder()
    {
        var charm = Charm(scaleMin: 30, scaleMax: 18);

        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, false, 20)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsAllowed(charm, false, false, 10)).IsFalse();
    }

    [Test]
    public async Task NullCharm_IsAllowed()
    {
        await Assert.That(GradeEnchantCharmRules.IsAllowed(null, true, false, 0)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.HasScaleRequirement(null)).IsFalse();
    }

    [Test]
    public async Task PassesGradeRequirement_ScaleGatedTemperAdditivesIgnoreGradeBand()
    {
        var temperAdditive = new ItemGradeEnchantingSupport
        {
            RequireGradeMin = 1,
            RequireGradeMax = 1,
            ReqScaleMinId = 0,
            ReqScaleMaxId = 30
        };

        await Assert.That(GradeEnchantCharmRules.PassesGradeRequirement(temperAdditive, 0)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.PassesGradeRequirement(temperAdditive, 11)).IsTrue();
    }

    [Test]
    public async Task PassesGradeRequirement_RegradeCharmsEnforceGradeBand()
    {
        var charm = new ItemGradeEnchantingSupport
        {
            RequireGradeMin = -1,
            RequireGradeMax = 6,
            ReqScaleMinId = 31,
            ReqScaleMaxId = 31
        };

        await Assert.That(GradeEnchantCharmRules.PassesGradeRequirement(charm, 6)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.PassesGradeRequirement(charm, 7)).IsFalse();
    }

    [Test]
    public async Task SlotSplit_RegradeRefusesScaleGated_TemperRequiresScaleGate()
    {
        var regradeCharm = Charm(scaleMin: 31, scaleMax: 31);
        var temperAdditive = Charm(scaleMin: 10, scaleMax: 30);

        await Assert.That(GradeEnchantCharmRules.IsRegradeSupport(regradeCharm)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsRegradeSupport(temperAdditive)).IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsTemperSupport(temperAdditive)).IsTrue();
        await Assert.That(GradeEnchantCharmRules.IsTemperSupport(regradeCharm)).IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsRegradeSupport(null)).IsFalse();
        await Assert.That(GradeEnchantCharmRules.IsTemperSupport(null)).IsFalse();
    }

    [Test]
    public async Task AdjustChance_AppliesFlatAndMulThenClamps()
    {
        await Assert.That(GradeEnchantCharmRules.AdjustChance(2000, null, null)).IsEqualTo(2000);
        await Assert.That(GradeEnchantCharmRules.AdjustChance(2000, 500, 50)).IsEqualTo(3500);
        await Assert.That(GradeEnchantCharmRules.AdjustChance(8000, 0, -100)).IsEqualTo(0);
        await Assert.That(GradeEnchantCharmRules.AdjustChance(9000, 2000, 0)).IsEqualTo(10000);
    }
}
