using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotMoveOriginRulesTests
{
    [Test]
    public async Task WaitFlag_RemembersTheShotPose()
    {
        await Assert.That(PlotMoveOriginRules.RemembersOrigin(1)).IsTrue();
        await Assert.That(PlotMoveOriginRules.RemembersOrigin(0)).IsFalse();
    }

    [Test]
    public async Task CasterBasis_Pins_OtherUnitDoesNot()
    {
        await Assert.That(PlotMoveOriginRules.PinsCasterStand(true, true)).IsTrue();
        await Assert.That(PlotMoveOriginRules.PinsCasterStand(false, true)).IsFalse();
        await Assert.That(PlotMoveOriginRules.PinsCasterStand(true, false)).IsFalse();
    }

    [Test]
    public async Task MillimetreNudge_StillSitsOnTheShot()
    {
        var onShot = PlotMoveOriginRules.StandSitsOnOrigin(true, 10.001f, 20f, 10f, 20f);
        var elsewhere = PlotMoveOriginRules.StandSitsOnOrigin(true, 18f, 20f, 10f, 20f);

        await Assert.That(onShot).IsTrue();
        await Assert.That(elsewhere).IsFalse();
        await Assert.That(PlotMoveOriginRules.ConeSearchesFromStand(onShot)).IsTrue();
        await Assert.That(PlotMoveOriginRules.ConeSearchesFromStand(elsewhere)).IsFalse();
    }
}
