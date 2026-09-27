using AAEmu.Game.Models.Game.CommonFarm;

namespace AAEmu.UnitTests.Game.Models.Game.CommonFarm;

/// <summary>
/// The admission decision every public-farm crop placement passes through.
/// </summary>
/// <remarks>
/// The regression these pin is a farm group that content gives no capacity row. That used to
/// resolve to a capacity of zero, and a zero capacity is not an empty farm: the count check passed
/// while the player held no crops and failed for every crop after the first, so the farm took
/// exactly one crop and then refused the rest for good with a "farm is full" message.
/// </remarks>
public class CommonFarmPlacementRulesTests
{
    [Test]
    public async Task AnUnconfiguredCapacityRefusesAtEveryPlantedCount()
    {
        // The defect, stated as a table. Whether the player holds no crop or ten, a farm whose size
        // content never defined must give the same answer. Before the fix the first row allowed the
        // crop and the rest reported a full farm.
        foreach (var planted in new[] { 0, 1, 2, 7, 64 })
        {
            var refusal = CommonFarmPlacementRules.Evaluate(
                capacityConfigured: false, capacity: 0, plantedCount: planted, doodadAllowed: true);

            await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.CapacityNotConfigured);
        }
    }

    [Test]
    public async Task AnUnconfiguredCapacityIsNotReportedAsAFullFarm()
    {
        // "No capacity row" and "capacity reached" are different faults and the client is told
        // different things. Collapsing them is the bug, so the two are pinned apart.
        var unconfigured = CommonFarmPlacementRules.Evaluate(false, 0, 0, doodadAllowed: true);
        var full = CommonFarmPlacementRules.Evaluate(true, 5, 5, doodadAllowed: true);

        await Assert.That(unconfigured).IsNotEqualTo(CommonFarmPlacementRefusal.CapacityReached);
        await Assert.That(full).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task AConfiguredFarmAllowsUpToItsCapacityAndRefusesAtIt()
    {
        // Capacity 5: five crops fit, the sixth does not. The boundary is the whole rule, so both
        // sides of it are asserted rather than only the refusal.
        for (var planted = 0; planted < 5; planted++)
        {
            var refusal = CommonFarmPlacementRules.Evaluate(true, 5, planted, doodadAllowed: true);

            await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.None);
        }

        var atCapacity = CommonFarmPlacementRules.Evaluate(true, 5, 5, doodadAllowed: true);
        await Assert.That(atCapacity).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task AnAllowedDoodadOutsideTheFarmGroupsListIsRefused()
    {
        var refusal = CommonFarmPlacementRules.Evaluate(true, 5, 0, doodadAllowed: false);

        await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.DoodadNotAllowed);
    }

    [Test]
    public async Task CapacityIsCheckedBeforeTheDoodadList()
    {
        // Order matters for the message the player gets. A full farm and a disallowed crop are both
        // refusals, and the capacity is the one that was wrong.
        var refusal = CommonFarmPlacementRules.Evaluate(true, 5, 5, doodadAllowed: false);

        await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task ANegativePlantedCountDoesNotWrapIntoAFullFarm()
    {
        // A count is never negative in practice. Compared as unsigned it would wrap to a very large
        // value, so the rule refuses it outright instead of letting the cast decide.
        var refusal = CommonFarmPlacementRules.Evaluate(true, 5, -1, doodadAllowed: true);

        await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }
}
