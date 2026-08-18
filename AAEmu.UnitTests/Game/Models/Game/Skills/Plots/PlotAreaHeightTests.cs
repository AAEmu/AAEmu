using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotAreaHeightTests
{
    [Test]
    public async Task GliderNitro_OverLand_KeepsAltitudePlusOffset()
    {
        // Skill 13435 plot 38: HeightOffset 10 m. Must not snap a gliding character to terrain.
        await Assert.That(PlotTargetInfo.ChoosePlotAreaHeight(
            anchorZ: 150f, ground: 140f, offsetMetres: 10f, previousIsFlyingNpc: false))
            .IsEqualTo(160f);
    }

    [Test]
    public async Task GliderNitro_OverSea_DoesNotSnapToSeabed()
    {
        await Assert.That(PlotTargetInfo.ChoosePlotAreaHeight(
            anchorZ: 110f, ground: 37f, offsetMetres: 10f, previousIsFlyingNpc: false,
            overWater: true, waterSurfaceZ: 100f))
            .IsEqualTo(120f);
    }

    [Test]
    public async Task FlyingPortalNpc_SnapsToTerrain()
    {
        await Assert.That(PlotTargetInfo.ChoosePlotAreaHeight(
            anchorZ: 172f, ground: 119f, offsetMetres: 10f, previousIsFlyingNpc: true))
            .IsEqualTo(119f);
    }

    [Test]
    public async Task LargeRayDropOffset_LandsOnFloor()
    {
        await Assert.That(PlotTargetInfo.ChoosePlotAreaHeight(
            anchorZ: 500f, ground: 119f, offsetMetres: 500f, previousIsFlyingNpc: false))
            .IsEqualTo(119f);
    }

    [Test]
    public async Task LargeRayDrop_OverWater_UsesSurface()
    {
        await Assert.That(PlotTargetInfo.ChoosePlotAreaHeight(
            anchorZ: 500f, ground: 37f, offsetMetres: 500f, previousIsFlyingNpc: false,
            overWater: true, waterSurfaceZ: 100f))
            .IsEqualTo(100f);
    }
}
