using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotItemUnlockRulesTests
{
    [Test]
    public async Task UpdateDetail_MustNotRideItemUnlock()
    {
        // Equipped plot-skill weapons went corrupt until re-equip when Unlock
        // carried an UpdateDetail ItemUpdate body.
        await Assert.That(PlotItemUnlockRules.AllowsUpdateDetailOnUnlock).IsFalse();
    }

    [Test]
    public async Task ItemPlot_DoesNotSendSkillEnded()
    {
        // SkillEnded without a client timeline is a failed skill event. That stripped a hide
        // the plot had just applied, and the hotbar slot stayed locked.
        await Assert.That(PlotItemUnlockRules.SendsSkillEnded).IsFalse();
    }

    [Test]
    public async Task Unlock_DoesNotRewriteTheChargeUseSkillClock()
    {
        // Writing the clock onto the piece did not free the hotbar shortcut, and the rewrite
        // dropped a hide the plot had just applied.
        await Assert.That(PlotItemUnlockRules.SendsChargeClock).IsFalse();
    }
}
