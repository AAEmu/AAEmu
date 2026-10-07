using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SlaveOccupancyRulesTests
{
    private const uint Hull = 1857;
    private const uint Figurehead = 1864;
    private const uint OtherHull = 1900;

    [Test]
    public async Task IsOnHull_StandingDirectlyOnTheHull()
    {
        // gcId named the hull itself: Transform.Parent is the hull, no grandparent.
        await Assert.That(SlaveOccupancyRules.IsOnHull(Hull, 0, Hull)).IsTrue();
    }

    [Test]
    public async Task IsOnHull_StandingOnAHullPartCountsAsThatHull()
    {
        // gcId named a figurehead, whose own parent is the hull.
        await Assert.That(SlaveOccupancyRules.IsOnHull(Figurehead, Hull, Hull)).IsTrue();
    }

    [Test]
    public async Task IsOnHull_OtherHullOrNoHullIsNotThisHull()
    {
        await Assert.That(SlaveOccupancyRules.IsOnHull(OtherHull, 0, Hull)).IsFalse();
        await Assert.That(SlaveOccupancyRules.IsOnHull(Figurehead, OtherHull, Hull)).IsFalse();
        await Assert.That(SlaveOccupancyRules.IsOnHull(0, 0, Hull)).IsFalse();
    }

    [Test]
    public async Task IsWithinStandingReach_OnlyTheHullUnderfootCounts()
    {
        // Directly underfoot, and a few metres further along the same deck.
        await Assert.That(SlaveOccupancyRules.IsWithinStandingReach(0f)).IsTrue();
        await Assert.That(SlaveOccupancyRules.IsWithinStandingReach(9f)).IsTrue();

        // A neighbouring hull across the water is not the one being stood on.
        await Assert.That(
            SlaveOccupancyRules.IsWithinStandingReach(SlaveOccupancyRules.StandingHintRadiusSquared + 1f))
            .IsFalse();

        // Beyond the reach in one axis alone is still out of reach.
        var beyond = SlaveOccupancyRules.StandingHintRadiusMetres + 1f;
        await Assert.That(SlaveOccupancyRules.IsWithinStandingReach(beyond * beyond)).IsFalse();
    }
}
