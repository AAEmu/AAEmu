using AAEmu.Game.Models.Game.Slaves;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SlaveStarterSeedRulesTests
{
    [Test]
    public async Task HasCreatedItem_SkipsAMissingTemplate()
    {
        // Compact rows 203, 238 and 247 name item 43000, which Create cannot make.
        await Assert.That(SlaveStarterSeedRules.HasCreatedItem(null)).IsFalse();
    }
}
