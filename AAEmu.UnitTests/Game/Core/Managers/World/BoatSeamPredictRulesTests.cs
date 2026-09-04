using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatSeamPredictRulesTests
{
    [Test]
    public async Task AheadMs_AddsReportAgeAndTheArmWait()
    {
        await Assert.That(BoatSeamPredictRules.AheadMs(200, 100)).IsEqualTo(300);
        await Assert.That(BoatSeamPredictRules.AheadMs(0, 100)).IsEqualTo(100);
        await Assert.That(BoatSeamPredictRules.AheadMs(200, 0)).IsEqualTo(200);
    }

    [Test]
    public async Task AheadMs_DoesNotInventFromAStaleOrEmptyReport()
    {
        await Assert.That(BoatSeamPredictRules.AheadMs(BoatSeamPredictRules.MaxPredictAgeMs, 100)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.AheadMs(BoatSeamPredictRules.MaxPredictAgeMs + 1, 0)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.AheadMs(-10, 100)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.AheadMs(200, -50)).IsEqualTo(200);
    }

    [Test]
    public async Task Advance_MovesAlongTheReportedVelocity()
    {
        // 15 m/s due east for 300 ms is 4.5 m. Quantised against the 30 m/s type-4 scale.
        var velX = (short)(15f / ShipMoveType.VelocityQuantizationScale * short.MaxValue);
        var (x, y, z) = BoatSeamPredictRules.Advance(10000f, 8000f, 100f, velX, 0, 0, 300);

        await Assert.That(x).IsEqualTo(10004.5f).Within(0.02f);
        await Assert.That(y).IsEqualTo(8000f);
        await Assert.That(z).IsEqualTo(100f);
    }

    [Test]
    public async Task OverlapAheadMs_DoesNotPlantASecondOfFollowWait()
    {
        await Assert.That(BoatSeamPredictRules.OverlapAheadMs(true, 17f, 50, 127)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.OverlapAheadMs(false, 17f, 50, 127)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.OverlapAheadMs(true, 1f, 50, 127)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.OverlapAheadMs(true, 17f, 50, 0)).IsEqualTo(0);
        await Assert.That(BoatSeamPredictRules.OverlapAheadMs(true, 17f, BoatSeamPredictRules.MaxPredictAgeMs, 127))
            .IsEqualTo(0);
    }

    [Test]
    public async Task LiveThrottle_PrefersAHeldHelmOverAZeroReport()
    {
        await Assert.That(BoatSeamPredictRules.LiveThrottle(0, 127, 0)).IsEqualTo((sbyte)127);
        await Assert.That(BoatSeamPredictRules.LiveThrottle(80, 127, 0)).IsEqualTo((sbyte)80);
        await Assert.That(BoatSeamPredictRules.LiveThrottle(0, 0, 40)).IsEqualTo((sbyte)40);
    }

    [Test]
    public async Task Advance_LeavesAStillPoseAlone()
    {
        var (x, y, z) = BoatSeamPredictRules.Advance(10000f, 8000f, 100f, 0, 0, 0, 1000);
        await Assert.That(x).IsEqualTo(10000f);
        await Assert.That(y).IsEqualTo(8000f);
        await Assert.That(z).IsEqualTo(100f);
    }
}
