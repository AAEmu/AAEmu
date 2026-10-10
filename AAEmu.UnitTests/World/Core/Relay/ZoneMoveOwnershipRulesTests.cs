using AAEmu.World.Core.Relay;

using GameTransform = AAEmu.Game.Models.Game.World.Transform.Transform;

namespace AAEmu.UnitTests.World.Core.Relay;

public class ZoneMoveOwnershipRulesTests
{
    [Test]
    public async Task OwningZoneAndCopy_MayMoveTheUnit()
    {
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(373, 102, 373, 102)).IsTrue();
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(183, 0, 183, 0)).IsTrue();
    }

    [Test]
    public async Task OpenWorldZone_MayNotMoveADungeonCopysUnit()
    {
        // A zone still holding a unit under an id the pool handed to a dungeon copy.
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(183, 0, 373, 102)).IsFalse();
    }

    [Test]
    public async Task OtherCopyOfTheSameDungeon_MayNotMoveTheUnit()
    {
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(373, 101, 373, 102)).IsFalse();
    }

    [Test]
    public async Task UnitWithoutAZone_AcceptsAnyReporter()
    {
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(183, 0, 0, 0)).IsTrue();
    }

    [Test]
    public async Task UnitWithoutAnInstance_ComparesTheZoneOnly()
    {
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(373, 102, 373, GameTransform.NoInstanceId)).IsTrue();
        await Assert.That(ZoneMoveOwnershipRules.IsOwnedBy(183, 0, 373, GameTransform.NoInstanceId)).IsFalse();
    }
}
