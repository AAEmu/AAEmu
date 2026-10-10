using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotRandomAreaRulesTests
{
    [Test]
    public async Task ZeroDistance_StaysOnAnchor()
    {
        await Assert.That(PlotRandomAreaRules.OffsetMetres(0, 0.5f)).IsEqualTo(0f);
    }

    [Test]
    public async Task SampleAtZero_LandsOnCenter()
    {
        // Gods' Whip Wave bolts must be allowed to land on the aim (enemy under the telegraph).
        await Assert.That(PlotRandomAreaRules.OffsetMetres(4000, 0f)).IsEqualTo(0f);
    }

    [Test]
    public async Task SampleAtOne_ReachesFullRadius()
    {
        await Assert.That(PlotRandomAreaRules.OffsetMetres(4000, 1f)).IsEqualTo(4f);
    }

    [Test]
    public async Task MidSample_IsInsideDiskNotOnRing()
    {
        var mid = PlotRandomAreaRules.OffsetMetres(4000, 0.25f);
        await Assert.That(mid).IsEqualTo(2f); // 4 * sqrt(0.25)
        await Assert.That(mid < 4f).IsTrue();
    }
}
