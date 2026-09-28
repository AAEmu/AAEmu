using AAEmu.Game.Models.Game.CommonFarm;

namespace AAEmu.UnitTests.Game.Models.Game.CommonFarm;

/// <summary>
/// The admission decision every public-farm crop placement passes through.
/// </summary>
/// <remarks>
/// The regression these pin is a farm tab that content gives no capacity row. That used to resolve
/// to a capacity of zero, and a zero capacity is not an empty farm: the count check passed while the
/// player held no crops and failed for every crop after the first, so the farm took exactly one crop
/// and then refused the rest for good with a "farm is full" message.
/// </remarks>
public class CommonFarmPlacementRulesTests
{
    [Test]
    public async Task AnUnconfiguredCapacityRefusesAtEveryPlantedCount()
    {
        // The defect, stated as a table. Whether the player holds no crop or ten, a farm tab whose
        // size content never defined must give the same answer. Before the fix the first row allowed
        // the crop and the rest reported a full farm.
        foreach (var planted in new[] { 0, 1, 2, 7, 64 })
        {
            var refusal = CommonFarmPlacementRules.Evaluate(
                doodadAllowed: true, capacityConfigured: false, capacity: 0, plantedCount: planted);

            await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.CapacityNotConfigured);
        }
    }

    [Test]
    public async Task AnUnconfiguredCapacityIsNotReportedAsAFullFarm()
    {
        // "No capacity row" and "capacity reached" are different faults and the client is told
        // different things. Collapsing them is the bug, so the two are pinned apart.
        var unconfigured = CommonFarmPlacementRules.Evaluate(true, false, 0, 0);
        var full = CommonFarmPlacementRules.Evaluate(true, true, 5, 5);

        await Assert.That(unconfigured).IsNotEqualTo(CommonFarmPlacementRefusal.CapacityReached);
        await Assert.That(full).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task AConfiguredTabAllowsUpToItsCapacityAndRefusesAtIt()
    {
        // Capacity 5: five crops fit, the sixth does not. The boundary is the whole rule, so both
        // sides of it are asserted rather than only the refusal.
        for (var planted = 0; planted < 5; planted++)
        {
            var refusal = CommonFarmPlacementRules.Evaluate(true, true, 5, planted);

            await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.None);
        }

        var atCapacity = CommonFarmPlacementRules.Evaluate(true, true, 5, 5);
        await Assert.That(atCapacity).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task ACropOutsideTheTabsAllowedListIsRefused()
    {
        var refusal = CommonFarmPlacementRules.Evaluate(false, true, 5, 0);

        await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.DoodadNotAllowed);
    }

    [Test]
    public async Task ACropOutsideTheAllowedListIsRefusedEvenWhenTheTabIsUnconfigured()
    {
        // The ordering, and the reason for it. The allowed list is the only question answerable
        // without a number content may not have written down, so it is asked first. Asked second, a
        // tab with no capacity row reported its content gap for a crop that was never plantable
        // there — a worse message for the player and a log line naming a content problem that does
        // not exist.
        var refusal = CommonFarmPlacementRules.Evaluate(false, false, 0, 0);

        await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.DoodadNotAllowed);
    }

    [Test]
    public async Task TheAllowedListIsAskedBeforeTheCapacityOfEitherKind()
    {
        // The ordering stated as one boundary with both sides of it, because the ordering is only
        // observable where the capacity can disagree: a crop that is not allowed in this tab is
        // refused as such whether the tab has a capacity, has none, or is full. Reported as "full",
        // the answer is true of the tab and useless about the crop; reported as a missing capacity,
        // it names a content problem that does not exist.
        var fullConfiguredTab = CommonFarmPlacementRules.Evaluate(false, true, 5, 5);
        var unconfiguredTab = CommonFarmPlacementRules.Evaluate(false, false, 0, 0);
        var emptyConfiguredTab = CommonFarmPlacementRules.Evaluate(false, true, 5, 0);

        await Assert.That(fullConfiguredTab).IsEqualTo(CommonFarmPlacementRefusal.DoodadNotAllowed);
        await Assert.That(unconfiguredTab).IsEqualTo(CommonFarmPlacementRefusal.DoodadNotAllowed);
        await Assert.That(emptyConfiguredTab).IsEqualTo(CommonFarmPlacementRefusal.DoodadNotAllowed);

        // And the other side of the boundary: the same tab with a crop that does belong in it must
        // still reach its capacity answer, or "allowed first" would be a rule that answers nothing.
        await Assert.That(CommonFarmPlacementRules.Evaluate(true, true, 5, 5))
            .IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task ANegativePlantedCountDoesNotWrapIntoAFullFarm()
    {
        // A count is never negative in practice. Compared as unsigned it would wrap to a very large
        // value, so the rule refuses it outright instead of letting the cast decide.
        var refusal = CommonFarmPlacementRules.Evaluate(true, true, 5, -1);

        await Assert.That(refusal).IsEqualTo(CommonFarmPlacementRefusal.CapacityReached);
    }

    [Test]
    public async Task EveryRefusalReasonIsReachableAndDistinct()
    {
        // Swept rather than spot-checked: an enum whose members are never all produced would leave a
        // branch unreachable, and a caller switch would carry a case nothing can reach.
        var produced = new HashSet<CommonFarmPlacementRefusal>
        {
            CommonFarmPlacementRules.Evaluate(true, true, 5, 0),
            CommonFarmPlacementRules.Evaluate(false, true, 5, 0),
            CommonFarmPlacementRules.Evaluate(true, false, 0, 0),
            CommonFarmPlacementRules.Evaluate(true, true, 5, 5)
        };

        await Assert.That(produced).IsEquivalentTo(Enum.GetValues<CommonFarmPlacementRefusal>());
    }
}
