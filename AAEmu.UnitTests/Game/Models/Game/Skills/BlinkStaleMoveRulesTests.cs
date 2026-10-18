using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BlinkStaleMoveRulesTests
{
    [Test]
    public async Task CastStand_FarFromLanding_IsStale()
    {
        await Assert.That(BlinkStaleMoveRules.IsStaleAwayFromLanding(
            100f, 200f, 150f, 250f)).IsTrue();
    }

    [Test]
    public async Task NearLanding_IsNotStale()
    {
        await Assert.That(BlinkStaleMoveRules.IsStaleAwayFromLanding(
            152f, 251f, 150f, 250f)).IsFalse();
    }

    [Test]
    public async Task ShouldDrop_OnlyInsideWindow_AndFar()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var until = now.AddMilliseconds(1000);

        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            now, until, 100f, 200f, 150f, 250f)).IsTrue();
        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            now, until, 152f, 251f, 150f, 250f)).IsFalse();
        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            until, until, 100f, 200f, 150f, 250f)).IsFalse();
        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            until.AddMilliseconds(1), until, 100f, 200f, 150f, 250f)).IsFalse();
    }

    [Test]
    public async Task MoveXyForLandingCompare_UsesWorldWhenCarrierLocal()
    {
        var (x, y) = BlinkStaleMoveRules.MoveXyForLandingCompare(
            packetX: 2f, packetY: 3f,
            positionIsCarrierLocal: true,
            worldMoveX: 13050f, worldMoveY: 11100f);
        await Assert.That(x).IsEqualTo(13050f);
        await Assert.That(y).IsEqualTo(11100f);

        (x, y) = BlinkStaleMoveRules.MoveXyForLandingCompare(
            packetX: 13050f, packetY: 11100f,
            positionIsCarrierLocal: false,
            worldMoveX: 0f, worldMoveY: 0f);
        await Assert.That(x).IsEqualTo(13050f);
        await Assert.That(y).IsEqualTo(11100f);
    }

    [Test]
    public async Task CarrierLocalNearLanding_WorldCompare_IsNotStale()
    {
        // Deck-local (2,3) under a hull at ~13048,11097 looks kilometres away if compared raw.
        var (wx, wy) = BlinkStaleMoveRules.MoveXyForLandingCompare(
            2f, 3f, positionIsCarrierLocal: true, worldMoveX: 150.5f, worldMoveY: 250.5f);
        await Assert.That(BlinkStaleMoveRules.IsStaleAwayFromLanding(wx, wy, 150f, 250f)).IsFalse();
        await Assert.That(BlinkStaleMoveRules.IsStaleAwayFromLanding(2f, 3f, 150f, 250f)).IsTrue();
    }
}
