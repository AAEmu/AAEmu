using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldContentFilterRulesTests
{
    [Test]
    public async Task IncludeFilterName_OmitsCharacterTest()
    {
        await Assert.That(WorldContentFilterRules.IncludeFilterName("character_test")).IsFalse();
        await Assert.That(WorldContentFilterRules.IncludeFilterName("character_fairy_male")).IsTrue();
        await Assert.That(WorldContentFilterRules.IncludeFilterName("th_dwarf_warborn")).IsTrue();
        await Assert.That(WorldContentFilterRules.IncludeFilterName(null)).IsFalse();
        await Assert.That(WorldContentFilterRules.IncludeFilterName("")).IsFalse();
    }
}
