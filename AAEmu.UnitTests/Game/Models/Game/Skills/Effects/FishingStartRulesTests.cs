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
    public async Task NonRodPlotsAreNotChangedByTheFishingAdmissionRule()
    {
        var template = new SkillTemplate
        {
            Plot = new Plot { Id = 1 },
            TargetOnlyWater = true
        };

        await Assert.That(FishingStartRules.ValidateStart(template, targetIsWater: false))
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

    private static SkillTemplate RodTemplate(uint plotId) => new()
    {
        Plot = new Plot { Id = plotId },
        TargetOnlyWater = true
    };
}
