using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// remove_all_doodad (type 140) deletes every doodad in range with no template or group filter, but a player's
/// house is not one of the barriers the rows describe and Doodad.Delete removes it from the database.
/// </summary>
public class RemoveAllDoodadRulesTests
{
    [Test]
    [Arguments(DoodadOwnerType.System)]
    [Arguments(DoodadOwnerType.Slave)]
    [Arguments(DoodadOwnerType.Character)]
    public async Task BarrierDoodads_AreRemoved(DoodadOwnerType ownerType)
    {
        await Assert.That(RemoveAllDoodadRules.ShouldRemove(ownerType)).IsTrue();
    }

    [Test]
    public async Task HousingDoodads_AreLeftAlone()
    {
        await Assert.That(RemoveAllDoodadRules.ShouldRemove(DoodadOwnerType.Housing)).IsFalse();
    }
}
