using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadGrowthScaleRulesTests
{
    [Test]
    public async Task TimerOnly_KeepsCurrentOrDefaultVisibleScale()
    {
        await Assert.That(DoodadGrowthScaleRules.IsTimerOnly(0, 0)).IsTrue();
        await Assert.That(DoodadGrowthScaleRules.ResolveScale(0, 0, 1f)).IsEqualTo(1f);
        await Assert.That(DoodadGrowthScaleRules.ResolveScale(0, 0, 0f)).IsEqualTo(1f);
        await Assert.That(DoodadGrowthScaleRules.ResolveScale(0, 0, 0.5f)).IsEqualTo(0.5f);
    }

    [Test]
    public async Task NormalPermille_ConvertsToFloatScale()
    {
        await Assert.That(DoodadGrowthScaleRules.IsTimerOnly(0, 1000)).IsFalse();
        await Assert.That(DoodadGrowthScaleRules.ResolveScale(1000, 2000, 1f)).IsEqualTo(1f);
        await Assert.That(DoodadGrowthScaleRules.ResolveScale(0, 1000, 1f)).IsEqualTo(0f);
    }
}
