using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Pins the public-farm placement decision table: a request is accepted only when it names a real
/// farm tab, targets an area that hosts that tab, and stays inside the group's content capacity.
/// Nothing here hardcodes a shipped capacity — the numbers are the caller-supplied inputs.
/// </summary>
public class PublicFarmPlacementRulesTests
{
    [Test]
    public async Task InvalidSentinelIsNotAUsableType()
    {
        await Assert.That(PublicFarmPlacementRules.IsUsableType(FarmType.Invalid)).IsFalse();
    }

    [Test]
    public async Task EveryDeclaredFarmTabIsUsable()
    {
        foreach (var type in Enum.GetValues<FarmType>())
        {
            if (type == FarmType.Invalid)
                continue;
            await Assert.That(PublicFarmPlacementRules.IsUsableType(type)).IsTrue();
        }
    }

    [Test]
    public async Task UnknownRequestTypeIsRefusedBeforeTheAreaIsEvenCompared()
    {
        // requestedType is the invalid sentinel, so it fails as an unknown type even when the area
        // would also mismatch — the order matters and is pinned here.
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Invalid, FarmType.Farm, maxCount: 5, alreadyPlanted: 0, requestedCount: 1);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.UnknownType);
    }

    [Test]
    public async Task ARequestForATabTheAreaDoesNotHostIsRefused()
    {
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Stable, maxCount: 5, alreadyPlanted: 0, requestedCount: 1);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.TypeMismatch);
    }

    [Test]
    public async Task AGroupWithNoContentCapacityRefusesEverything()
    {
        // maxCount == 0 is what a missing farm_groups row looks like; it must refuse, not default.
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Farm, maxCount: 0, alreadyPlanted: 0, requestedCount: 1);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.NoContent);
    }

    [Test]
    public async Task ARequestForNothingIsRefused()
    {
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Farm, maxCount: 5, alreadyPlanted: 0, requestedCount: 0);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.EmptyRequest);
    }

    [Test]
    public async Task AValidRequestFitsAndIsAccepted()
    {
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Farm, maxCount: 5, alreadyPlanted: 0, requestedCount: 1);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.None);
    }

    [Test]
    public async Task ARequestThatExactlyFillsTheRemainingCapacityIsAccepted()
    {
        // already 3, capacity 5, ask for 2 -> exactly fits.
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Farm, maxCount: 5, alreadyPlanted: 3, requestedCount: 2);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.None);
    }

    [Test]
    public async Task OneOverTheRemainingCapacityIsRefused()
    {
        // already 3, capacity 5, ask for 3 -> one too many.
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Farm, maxCount: 5, alreadyPlanted: 3, requestedCount: 3);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.CountOver);
    }

    [Test]
    public async Task ACountNearUIntMaxCannotWrapIntoFitting()
    {
        // Without widening, alreadyPlanted + requestedCount would wrap and look like it fits.
        var failure = PublicFarmPlacementRules.ValidatePlacement(
            FarmType.Farm, FarmType.Farm, maxCount: 5, alreadyPlanted: 4, requestedCount: uint.MaxValue);

        await Assert.That(failure).IsEqualTo(PublicFarmPlaceFailure.CountOver);
    }

    [Test]
    public async Task AnAcceptMapsToNoErrorAndCountOverMapsToTheFarmCountError()
    {
        await Assert.That(PublicFarmPlacementRules.ToErrorMessage(PublicFarmPlaceFailure.None)).IsNull();
        await Assert.That(PublicFarmPlacementRules.ToErrorMessage(PublicFarmPlaceFailure.CountOver))
            .IsEqualTo(ErrorMessageType.CommonFarmCountOver);
    }

    [Test]
    public async Task EveryRefusalMapsToADefinitiveFarmError()
    {
        await Assert.That(PublicFarmPlacementRules.ToErrorMessage(PublicFarmPlaceFailure.UnknownType))
            .IsEqualTo(ErrorMessageType.CommonFarmNotAllowedType);
        await Assert.That(PublicFarmPlacementRules.ToErrorMessage(PublicFarmPlaceFailure.TypeMismatch))
            .IsEqualTo(ErrorMessageType.CommonFarmNotAllowedType);
        await Assert.That(PublicFarmPlacementRules.ToErrorMessage(PublicFarmPlaceFailure.NoContent))
            .IsEqualTo(ErrorMessageType.CommonFarmNotAllowedType);
        await Assert.That(PublicFarmPlacementRules.ToErrorMessage(PublicFarmPlaceFailure.EmptyRequest))
            .IsEqualTo(ErrorMessageType.CommonFarmNotAllowedType);
    }
}
