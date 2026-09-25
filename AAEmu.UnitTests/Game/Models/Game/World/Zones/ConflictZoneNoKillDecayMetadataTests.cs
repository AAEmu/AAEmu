using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

public class ConflictZoneNoKillDecayMetadataTests
{
    [Test]
    public async Task AllZeroMetadata_IsInactiveAndRemainsObservable()
    {
        var metadata = new ConflictZoneNoKillDecayMetadata(
            14,
            Enumerable.Repeat(0, ConflictZoneNoKillDecayMetadata.TroubleStateCount));

        await Assert.That(metadata.IsConfigured).IsFalse();
        await Assert.That(metadata.NoKillMinutesByTroubleState).IsEquivalentTo(new[] { 0, 0, 0, 0, 0 });
        await Assert.That(metadata.ToDiagnostic()).Contains("configured=False");
    }

    [Test]
    public async Task ConfiguredMetadata_PreservesValuesWithoutClaimingRuntimeApplication()
    {
        var metadata = new ConflictZoneNoKillDecayMetadata(17, new[] { 3, 0, 7, 0, 0 });

        await Assert.That(metadata.IsConfigured).IsTrue();
        await Assert.That(metadata.GetMinutes(ZoneConflictType.Tension)).IsEqualTo(3);
        await Assert.That(metadata.GetMinutes(ZoneConflictType.Crisis)).IsEqualTo(0);
        await Assert.That(metadata.ToDiagnostic()).Contains("application=deferred");
    }

    [Test]
    public void WrongShapeOrNegativeValue_FailsLoudly()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ConflictZoneNoKillDecayMetadata(14, new[] { 0, 0 }));
        Assert.Throws<InvalidOperationException>(() =>
            new ConflictZoneNoKillDecayMetadata(14, new[] { 0, -1, 0, 0, 0 }));
    }

    [Test]
    public void TimedStateLookup_FailsLoudly()
    {
        var metadata = new ConflictZoneNoKillDecayMetadata(
            14,
            Enumerable.Repeat(0, ConflictZoneNoKillDecayMetadata.TroubleStateCount));

        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.GetMinutes(ZoneConflictType.Conflict));
    }

    [Test]
    public void ZoneConflict_RequiresBoundMetadataForDiagnostics()
    {
        var conflict = new ZoneConflict(new ZoneGroup { Id = 14 }) { ZoneGroupId = 14 };

        Assert.Throws<InvalidOperationException>(() => conflict.GetNoKillDecayDiagnostic());
    }

    [Test]
    public void ZoneConflict_RejectsMetadataForAnotherZone()
    {
        var conflict = new ZoneConflict(new ZoneGroup { Id = 14 }) { ZoneGroupId = 14 };
        var metadata = new ConflictZoneNoKillDecayMetadata(
            15,
            Enumerable.Repeat(0, ConflictZoneNoKillDecayMetadata.TroubleStateCount));

        Assert.Throws<InvalidOperationException>(() => conflict.BindNoKillDecayMetadata(metadata));
    }
}
