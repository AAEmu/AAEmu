using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public sealed class FishingStartRulesTests
{
    [Test]
    public async Task RodPlotsRequireAWaterTarget()
    {
        var bait = RodTemplate(SportFishCombat.BaitFishingPlotId);
        var sport = RodTemplate(SportFishCombat.SportFishingPlotId);

        await Assert.That(FishingStartRules.ValidateStart(bait, targetIsWater: true))
            .IsEqualTo(SkillResult.Success);
        await Assert.That(FishingStartRules.ValidateStart(sport, targetIsWater: true))
            .IsEqualTo(SkillResult.Success);
        await Assert.That(FishingStartRules.ValidateStart(bait, targetIsWater: false))
            .IsEqualTo(SkillResult.InvalidTarget);
        await Assert.That(FishingStartRules.ValidateStart(sport, targetIsWater: false))
            .IsEqualTo(SkillResult.InvalidTarget);
    }

    [Test]
    public async Task EveryRodCastIsGatedNotJustTheOriginalTwoPlots()
    {
        // All 36 shipped skills with target_only_water = true and a plot are rod casts, across 34
        // plots. Gating only 809/821 let a dry-target cast through for the other 32.
        var bowfish = RodTemplate(840);
        var secret = RodTemplate(2255);
        var mirrorKingdom = RodTemplate(1630);

        await Assert.That(FishingStartRules.RequiresWater(bowfish)).IsTrue();
        await Assert.That(FishingStartRules.RequiresWater(secret)).IsTrue();
        await Assert.That(FishingStartRules.RequiresWater(mirrorKingdom)).IsTrue();
        await Assert.That(FishingStartRules.ValidateStart(bowfish, targetIsWater: false))
            .IsEqualTo(SkillResult.InvalidTarget);
        await Assert.That(FishingStartRules.ValidateStart(secret, targetIsWater: false))
            .IsEqualTo(SkillResult.InvalidTarget);
        await Assert.That(FishingStartRules.ValidateStart(mirrorKingdom, targetIsWater: true))
            .IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task PlotlessWaterFlaggedSkillsAreNotGated()
    {
        // 51 of the 87 shipped skills flagged target_only_water have no plot and are not rod
        // casts - underwater-usable self buffs (experience, drop-rate, death-penalty, honor
        // potions) plus bait scatter and release. Gating them would reject legitimate use.
        var experiencePotion = new SkillTemplate { TargetOnlyWater = true };
        var dropRatePotion = new SkillTemplate { TargetOnlyWater = true, Plot = null };

        await Assert.That(FishingStartRules.RequiresWater(experiencePotion)).IsFalse();
        await Assert.That(FishingStartRules.RequiresWater(dropRatePotion)).IsFalse();
        await Assert.That(FishingStartRules.ValidateStart(experiencePotion, targetIsWater: false))
            .IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task NoPlotAndNoWaterFlagAreNeverGated()
    {
        await Assert.That(FishingStartRules.RequiresWater(null)).IsFalse();
        await Assert.That(FishingStartRules.RequiresWater(new SkillTemplate())).IsFalse();
        await Assert.That(FishingStartRules.ValidateStart(null, targetIsWater: false))
            .IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task RodPlotWithoutTargetOnlyWaterIsNotGated()
    {
        var template = new SkillTemplate
        {
            Plot = new Plot { Id = SportFishCombat.BaitFishingPlotId },
            TargetOnlyWater = false
        };

        await Assert.That(FishingStartRules.RequiresWater(template)).IsFalse();
        await Assert.That(FishingStartRules.ValidateStart(template, targetIsWater: false))
            .IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task WaterProbeDescentIsShortAndPositive()
    {
        // The probe tolerance is a geometric margin, not a content value, and it must be a
        // small positive descent: enough to forgive a surface hit, not enough to make a cast
        // at a distant shoreline land in water.
        await Assert.That(FishingStartRules.WaterProbeDescentMetres).IsGreaterThan(0f);
        await Assert.That(FishingStartRules.WaterProbeDescentMetres).IsLessThan(10f);
    }

    private static SkillTemplate RodTemplate(uint plotId) => new()
    {
        Plot = new Plot { Id = plotId },
        TargetOnlyWater = true
    };
}
