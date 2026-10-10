using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

public class TowerDefCopyOwnershipRulesTests
{
    [Test]
    public async Task SameCopy_WorldEvent_IgnoresSiblingCopies()
    {
        await Assert.That(TowerDefCopyOwnershipRules.SameCopy(0, 0)).IsTrue();
        await Assert.That(TowerDefCopyOwnershipRules.SameCopy(0, 7)).IsFalse();
    }

    [Test]
    public async Task SameCopy_Copy_RequiresTheCopyId()
    {
        await Assert.That(TowerDefCopyOwnershipRules.SameCopy(7, 7)).IsTrue();
        await Assert.That(TowerDefCopyOwnershipRules.SameCopy(7, 8)).IsFalse();
        await Assert.That(TowerDefCopyOwnershipRules.SameCopy(7, 0)).IsFalse();
    }

    [Test]
    public async Task InstanceRunOwnedByHost_OnlyThatCopy()
    {
        await Assert.That(TowerDefCopyOwnershipRules.InstanceRunOwnedByHost(7, 7)).IsTrue();
        await Assert.That(TowerDefCopyOwnershipRules.InstanceRunOwnedByHost(7, 8)).IsFalse();
        await Assert.That(TowerDefCopyOwnershipRules.InstanceRunOwnedByHost(7, 0)).IsFalse();
        await Assert.That(TowerDefCopyOwnershipRules.InstanceRunOwnedByHost(0, 0)).IsFalse();
        await Assert.That(TowerDefCopyOwnershipRules.InstanceRunOwnedByHost(0, 7)).IsFalse();
    }
}
