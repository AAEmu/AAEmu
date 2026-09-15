using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.World.Transform;

public class HousingZoneRetentionRulesTests
{
    [Test]
    public async Task StaysWhenThePositionIsStillInsideTheHousingZone()
    {
        // House 742: base zone 213 resolves, but the position is inside housing zone 207.
        uint[] housingZonesAtPosition = [207];

        await Assert.That(HousingZoneRetentionRules.ShouldSuppressChange(207, 213, housingZonesAtPosition)).IsTrue();
    }

    [Test]
    public async Task DoesNotSuppressWhenTheCharacterLeftTheHousingZone()
    {
        // Position is in open world now: the base zone is the only answer and the change is real.
        uint[] housingZonesAtPosition = [];

        await Assert.That(HousingZoneRetentionRules.ShouldSuppressChange(207, 213, housingZonesAtPosition)).IsFalse();
    }

    [Test]
    public async Task DoesNotSuppressADifferentHousingZone()
    {
        // Moving into a neighbouring housing area is a real zone change.
        uint[] housingZonesAtPosition = [208];

        await Assert.That(HousingZoneRetentionRules.ShouldSuppressChange(207, 208, housingZonesAtPosition)).IsFalse();
    }

    [Test]
    public async Task DoesNotSuppressWhenNothingChanged()
    {
        uint[] housingZonesAtPosition = [207];

        await Assert.That(HousingZoneRetentionRules.ShouldSuppressChange(207, 207, housingZonesAtPosition)).IsFalse();
    }

    [Test]
    public async Task UnknownOldZoneIsNeverRetained()
    {
        // A character with no zone yet (0) must not have one invented for them.
        uint[] housingZonesAtPosition = [0, 207];

        await Assert.That(HousingZoneRetentionRules.ShouldSuppressChange(0, 213, housingZonesAtPosition)).IsFalse();
    }
}
