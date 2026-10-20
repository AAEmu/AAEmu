using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotAreaStandRulesTests
{
    private const int RegionSize = WorldManager.REGION_SIZE;

    [Test]
    public async Task SameCell_StandsInTheCopiedRegion()
    {
        // Caster in cell (0, 0), stand slid 26 m — still inside the 64 m cell.
        await Assert.That(PlotAreaStandRules.StandsInRegion(0, 0, 26f, 10f, RegionSize)).IsTrue();
    }

    [Test]
    public async Task NextCell_CopiedRegionDoesNotContainTheStand()
    {
        // Caster near the far side of cell (0, 0) at x=50, stand 26 m ahead lands in cell (1, 0).
        await Assert.That(PlotAreaStandRules.StandsInRegion(0, 0, 76f, 10f, RegionSize)).IsFalse();
        await Assert.That(PlotAreaStandRules.StandsInRegion(1, 0, 76f, 10f, RegionSize)).IsTrue();
    }

    [Test]
    public async Task ZeroRegionSize_IsNotACell()
    {
        await Assert.That(PlotAreaStandRules.StandsInRegion(0, 0, 10f, 10f, 0)).IsFalse();
    }
}
