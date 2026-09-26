using AAEmu.Game.Models.Game.Skills;
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
        // 52 of the 89 shipped skills flagged target_only_water have no plot and are not rod casts -
        // underwater-usable self buffs (experience, drop-rate, death-penalty, honor potions) plus
        // bait scatter and release. 47 of them are position-targeted, so the missing plot has to be
        // what keeps them out; gating them would reject legitimate use.
        var experiencePotion = new SkillTemplate { TargetOnlyWater = true, TargetType = SkillTargetType.Pos };
        var dropRatePotion = new SkillTemplate
        {
            TargetOnlyWater = true,
            TargetType = SkillTargetType.Pos,
            Plot = null
        };

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
            TargetOnlyWater = false,
            TargetType = SkillTargetType.Pos
        };

        await Assert.That(FishingStartRules.RequiresWater(template)).IsFalse();
        await Assert.That(FishingStartRules.ValidateStart(template, targetIsWater: false))
            .IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task KrakenInkSprayIsNotGated()
    {
        // 27200 and 49524 are the two flagged skills with a plot that are not rod casts: they
        // carry target_only_water because they are used from water, but they target a hostile
        // unit. Gating them rejected the spray whenever the Kraken aimed at a player on a deck.
        var inkSpray = new SkillTemplate
        {
            Plot = new Plot { Id = 1587 },
            TargetOnlyWater = true,
            TargetType = SkillTargetType.Hostile
        };

        await Assert.That(FishingStartRules.RequiresWater(inkSpray)).IsFalse();
        await Assert.That(FishingStartRules.ValidateStart(inkSpray, targetIsWater: false))
            .IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task WaterSurfaceToleranceIsShortAndPositive()
    {
        // The tolerance is a geometric band, not a content value. It forgives a wave crest, so it
        // must be positive, and it is compared against the surface rather than descended by, so
        // it must stay well under the 2 m a descent used to reach - a margin that big accepts
        // dry ground 2 m above sea level.
        await Assert.That(FishingStartRules.WaterSurfaceToleranceMetres).IsGreaterThan(0f);
        await Assert.That(FishingStartRules.WaterSurfaceToleranceMetres).IsLessThan(2f);
    }

    private static SkillTemplate RodTemplate(uint plotId) => new()
    {
        Plot = new Plot { Id = plotId },
        TargetOnlyWater = true,
        TargetType = SkillTargetType.Pos
    };
}
