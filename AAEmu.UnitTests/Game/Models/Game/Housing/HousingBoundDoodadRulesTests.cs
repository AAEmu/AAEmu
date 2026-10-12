using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

public class HousingBoundDoodadRulesTests
{
    [Test]
    public async Task ShouldTrackBoundOnHouse_TracksEveryTemplateBinding()
    {
        await Assert.That(HousingBoundDoodadRules.ShouldTrackBoundOnHouse(true)).IsTrue();
        await Assert.That(HousingBoundDoodadRules.ShouldTrackBoundOnHouse(false)).IsFalse();
    }

    [Test]
    public async Task ShouldPersistBinding_WorldgateSurvivesWhenGlobalFlagOff()
    {
        await Assert.That(HousingBoundDoodadRules.ShouldPersistBinding(false, isWorldgate: true)).IsTrue();
        await Assert.That(HousingBoundDoodadRules.ShouldPersistBinding(false, isWorldgate: false)).IsFalse();
        await Assert.That(HousingBoundDoodadRules.ShouldPersistBinding(true, isWorldgate: false)).IsTrue();
    }
}
