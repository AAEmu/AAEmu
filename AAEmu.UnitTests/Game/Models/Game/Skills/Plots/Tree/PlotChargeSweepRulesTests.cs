using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotChargeSweepRulesTests
{
    [Test]
    public async Task BesideTheMiddle_IsOnTheSegmentAndOffBothEnds()
    {
        const float radius = 6f;
        const float radiusSq = radius * radius;
        const float start = 5.5f;
        const float end = 16f;
        const float beside = 5.99f;
        var mid = (start + end) / 2f;

        await Assert.That(PlotChargeSweepRules.WithinRadiusSq(mid, beside, start, 0f, end, 0f, radiusSq)).IsTrue();
        await Assert.That(PlotChargeSweepRules.WithinRadiusSq(mid, beside, start, 0f, start, 0f, radiusSq)).IsFalse();
        await Assert.That(PlotChargeSweepRules.WithinRadiusSq(mid, beside, end, 0f, end, 0f, radiusSq)).IsFalse();
    }

    [Test]
    public async Task WellOffThePath_IsOutside()
    {
        await Assert.That(PlotChargeSweepRules.WithinRadiusSq(10f, 8f, 5.5f, 0f, 16f, 0f, 36f)).IsFalse();
    }

    [Test]
    public async Task ShouldSweepHop_OnlyRepeatingSlides()
    {
        await Assert.That(PlotChargeSweepRules.ShouldSweepHop(tickets: 2, distanceMm: 5000)).IsTrue();
        await Assert.That(PlotChargeSweepRules.ShouldSweepHop(tickets: 5, distanceMm: 1000)).IsTrue();
        await Assert.That(PlotChargeSweepRules.ShouldSweepHop(tickets: 1, distanceMm: 22000)).IsFalse();
        await Assert.That(PlotChargeSweepRules.ShouldSweepHop(tickets: 2, distanceMm: 0)).IsFalse();
    }
}
