using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatSeamHandoffRulesTests
{
    private static ShipMoveType Body(float x, float y, short velX, short velY = 0) => new()
    {
        Type = MoveTypeEnum.Ship,
        X = x,
        Y = y,
        Z = 100f,
        VelX = velX,
        VelY = velY,
        Throttle = 127,
        Steering = 0,
        Rpm = 40,
        ZoneId = 186
    };

    [Test]
    public async Task Capture_RequiresALastReportAndADestination()
    {
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            null, 1000, null, 0, 1, 186, 218, 1100, 1100, 127, out _)).IsFalse();
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10, 20, 0), 1000, null, 0, 1, 186, 0, 1100, 1100, 127, out _)).IsFalse();
    }

    [Test]
    public async Task Propagate_AdvancesPositionByVelocityTimesDelta()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(15f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10000f, 8000f, velX), 10_000, null, 0, 1, 186, 218, 10_000, 300, 127, out var snap)).IsTrue();

        var (x, y, z, vx, _, _) = BoatSeamHandoffRules.Propagate(snap);

        await Assert.That(x).IsEqualTo(10004.5f).Within(0.02f);
        await Assert.That(y).IsEqualTo(8000f);
        await Assert.That(z).IsEqualTo(100f);
        await Assert.That(vx).IsEqualTo(velX);
        await Assert.That(BoatSeamHandoffRules.DeltaMs(snap)).IsEqualTo(300);
    }

    [Test]
    public async Task Propagate_AddsConstantAccelerationOnce()
    {
        var v0 = BoatSeamHandoffRules.EncodeVelMetresPerSecond(10f);
        var v1 = BoatSeamHandoffRules.EncodeVelMetresPerSecond(12f);
        var previous = Body(10000f, 8000f, v0);
        var last = Body(10002f, 8000f, v1);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            last, 10_200, previous, 10_000, 1, 186, 218, 10_200, 1000, 127, out var snap)).IsTrue();

        await Assert.That(snap.AccelX).IsEqualTo(10f).Within(0.05f);

        var (x, _, _, vx, _, _) = BoatSeamHandoffRules.Propagate(snap);
        // Δt = 1 s: x = 10002 + 12 + ½·10·1 = 10019, v = 22.
        await Assert.That(x).IsEqualTo(10019f).Within(0.05f);
        await Assert.That(BoatSeamPredictRules.DecodeVelMetresPerSecond(vx)).IsEqualTo(22f).Within(0.05f);
    }

    [Test]
    public async Task Propagate_IsIdempotentForTheSameSnapshot()
    {
        var velY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(17f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(13000f, 10000f, 0, velY), 5_000, null, 0, 3, 218, 186, 5_050, 1100, 127, out var snap)).IsTrue();

        var first = BoatSeamHandoffRules.Propagate(snap);
        var second = BoatSeamHandoffRules.Propagate(snap);

        await Assert.That(first.X).IsEqualTo(second.X);
        await Assert.That(first.Y).IsEqualTo(second.Y);
        await Assert.That(first.VelY).IsEqualTo(second.VelY);
    }

    [Test]
    public async Task Accel_IsZeroWhenTheSamplePairIsMissingOrStale()
    {
        var last = Body(0, 0, BoatSeamHandoffRules.EncodeVelMetresPerSecond(15f));
        var previous = Body(0, 0, 0);
        await Assert.That(BoatSeamHandoffRules.AccelMetresPerSecondSquared(null, 0, last, 1000))
            .IsEqualTo((0f, 0f, 0f));
        await Assert.That(BoatSeamHandoffRules.AccelMetresPerSecondSquared(previous, 1000, last, 1050))
            .IsEqualTo((0f, 0f, 0f));
        await Assert.That(BoatSeamHandoffRules.AccelMetresPerSecondSquared(previous, 1000, last, 5000))
            .IsEqualTo((0f, 0f, 0f));
    }

    [Test]
    public async Task Accel_DropsAGlitchJump()
    {
        var previous = Body(0, 0, 0);
        var last = Body(0, 0, short.MaxValue);
        var (ax, ay, az) = BoatSeamHandoffRules.AccelMetresPerSecondSquared(previous, 1000, last, 1200);
        await Assert.That(ax).IsEqualTo(0f);
        await Assert.That(ay).IsEqualTo(0f);
        await Assert.That(az).IsEqualTo(0f);
    }

    [Test]
    public async Task IsForActivation_RejectsAStaleEpochOrOtherZone()
    {
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(1, 2, 0), 1, null, 0, 4, 186, 218, 1, 100, 127, out var snap)).IsTrue();

        await Assert.That(BoatSeamHandoffRules.IsForActivation(snap, 218, 4)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.IsForActivation(snap, 218, 5)).IsFalse();
        await Assert.That(BoatSeamHandoffRules.IsForActivation(snap, 186, 4)).IsFalse();
    }

    [Test]
    public async Task WithActivationTick_RebindsDeltaWithoutChangingTheFrozenState()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(10f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10000f, 8000f, velX), 10_000, null, 0, 1, 186, 218, 10_000, 100, 127, out var snap))
            .IsTrue();

        var rebound = BoatSeamHandoffRules.WithActivationTick(snap, 10_200);
        var (x, _, _, _, _, _) = BoatSeamHandoffRules.Propagate(rebound);

        await Assert.That(BoatSeamHandoffRules.DeltaMs(rebound)).IsEqualTo(200);
        await Assert.That(x).IsEqualTo(10002f).Within(0.02f);
        await Assert.That(rebound.X).IsEqualTo(snap.X);
        await Assert.That(rebound.VelX).IsEqualTo(snap.VelX);
        await Assert.That(rebound.Epoch).IsEqualTo(snap.Epoch);
        await Assert.That(rebound.Sequence).IsEqualTo(snap.Sequence + 1);
    }

    [Test]
    public async Task HasReachedClientBridge_RejectsTheCreatePoseBehindThePlant()
    {
        // 186→218 at 03:07:19: last A / B body stayed at Create, client bridge froze ~2.5 m ahead.
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(-4f);
        var velY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(-17.2f);
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            13008.64f, 9919.79f, 13008.01f, 9917.34f, velX, velY)).IsFalse();
        await Assert.That(BoatSeamHandoffRules.AlongTrackMetres(
            13008.64f, 9919.79f, 13008.01f, 9917.34f, velX, velY)).IsLessThan(-2f);
    }

    [Test]
    public async Task HasReachedClientBridge_AcceptsAtOrPastThePlant()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(-4f);
        var velY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(-17.2f);
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            13008.01f, 9917.34f, 13008.01f, 9917.34f, velX, velY)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            13007.82f, 9916.33f, 13008.01f, 9917.34f, velX, velY)).IsTrue();
    }

    [Test]
    public async Task HasReachedClientBridge_AllowsHalfAMetreOfQuantization()
    {
        var velY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(-17f);
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            10000f, 8000.4f, 10000f, 8000f, 0, velY)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            10000f, 8000.6f, 10000f, 8000f, 0, velY)).IsFalse();
    }

    [Test]
    public async Task HasReachedClientBridge_UsesDistanceWhenTheSnapshotIsStill()
    {
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            10000.3f, 8000f, 10000f, 8000f, 0, 0)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            10002f, 8000f, 10000f, 8000f, 0, 0)).IsFalse();
    }

    [Test]
    public async Task HasReachedClientBridge_ReadsTheFrozenPlantFromTheBoundSnapshot()
    {
        var velY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(-17.6f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(13008.64f, 9919.79f, 0, velY), 10_000, null, 0, 9, 186, 218, 10_000, 0, 127, out var snap))
            .IsTrue();
        var bound = BoatSeamHandoffRules.WithActivationTick(snap, 10_140);
        var (plantX, plantY, _, _, _, _) = BoatSeamHandoffRules.Propagate(bound);

        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            bound, 13008.64f, 9919.79f, 10_400)).IsFalse();
        await Assert.That(BoatSeamHandoffRules.HasReachedClientBridge(
            bound, plantX, plantY, 10_400)).IsTrue();
    }

    [Test]
    public async Task Capture_ActivationIncludesTheKnownCreateToArmWait()
    {
        var velY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(13.1f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(12950.2f, 9919.8f, 0, velY), 10_000, null, 0, 1, 149, 218, 10_063, 100, 127,
            out var snap)).IsTrue();

        await Assert.That(BoatSeamHandoffRules.DeltaMs(snap)).IsEqualTo(163);
        var (_, y, _, _, velOut, _) = BoatSeamHandoffRules.Propagate(snap);
        await Assert.That(y).IsEqualTo(9919.8f + 13.1f * 0.163f).Within(0.05f);
        await Assert.That(BoatSeamPredictRules.DecodeVelMetresPerSecond(velOut)).IsEqualTo(13.1f).Within(0.05f);
    }

    [Test]
    public async Task PlannedActivationTick_DoesNotAdvanceAgainWhenArmIsLate()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(10f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10000f, 8000f, velX), 10_000, null, 0, 1, 186, 218, 10_000, 100, 127, out var snap))
            .IsTrue();

        await Assert.That(snap.ActivationTickMs).IsEqualTo(10_100);
        await Assert.That(BoatSeamHandoffRules.PlannedActivationTick(snap, 10_250)).IsEqualTo(10_100);
        await Assert.That(BoatSeamHandoffRules.PlannedActivationTick(snap, 10_050)).IsEqualTo(10_100);
    }

    [Test]
    public async Task ClientBridgeTick_DoesNotPredictPastThePlantedActivation()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(10f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10000f, 8000f, velX), 10_000, null, 0, 1, 186, 218, 10_000, 100, 127, out var snap))
            .IsTrue();

        await Assert.That(snap.Sequence).IsEqualTo(1u);
        await Assert.That(snap.ActivationTickMs).IsEqualTo(10_100);
        await Assert.That(BoatSeamHandoffRules.ClientBridgeTick(snap, 10_050)).IsEqualTo(10_050);
        await Assert.That(BoatSeamHandoffRules.ClientBridgeTick(snap, 10_300)).IsEqualTo(10_100);
        var bound = BoatSeamHandoffRules.WithActivationTick(snap, 10_200);
        await Assert.That(bound.Sequence).IsEqualTo(2u);
        await Assert.That(BoatSeamHandoffRules.ClientBridgeTick(bound, 10_150)).IsEqualTo(10_150);
        await Assert.That(BoatSeamHandoffRules.ClientBridgeTick(bound, 10_400)).IsEqualTo(10_200);
    }

    [Test]
    public async Task EvaluateAt_PredictsWithoutChangingThePlantedActivation()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(10f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10000f, 8000f, velX), 10_000, null, 0, 1, 186, 218, 10_000, 100, 127, out var snap))
            .IsTrue();

        var (x, _, _, _, _, _) = BoatSeamHandoffRules.EvaluateAt(snap, 10_300);
        await Assert.That(x).IsEqualTo(10003f).Within(0.02f);
        await Assert.That(BoatSeamHandoffRules.DeltaMs(snap)).IsEqualTo(100);
        await Assert.That(snap.Sequence).IsEqualTo(1u);
    }

    [Test]
    public async Task EvaluateRotation_LeavesAStillHeadingAlone()
    {
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(1, 2, 0), 1, null, 0, 1, 186, 218, 1, 100, 127, out var snap)).IsTrue();
        snap = snap with { RotationX = 1000, RotationY = 2000, RotationZ = 3000 };

        var (rx, ry, rz) = BoatSeamHandoffRules.EvaluateRotation(snap, 1_100);
        await Assert.That(rx).IsEqualTo((short)1000);
        await Assert.That(ry).IsEqualTo((short)2000);
        await Assert.That(rz).IsEqualTo((short)3000);
    }

    [Test]
    public async Task EvaluateRotation_AdvancesAYawRate()
    {
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(1, 2, 0), 10_000, null, 0, 1, 186, 218, 10_000, 0, 127, out var snap)).IsTrue();
        snap = snap with { AngVelZ = 1f, ActivationTickMs = 11_000 };

        var before = BoatSeamHandoffRules.EvaluateRotation(snap, 10_000);
        var after = BoatSeamHandoffRules.EvaluateRotation(snap, 11_000);
        await Assert.That(after.RotationZ).IsNotEqualTo(before.RotationZ);
    }

    [Test]
    public async Task IsClientBridge_NeedsADestination()
    {
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(1, 2, 0), 1, null, 0, 1, 186, 218, 1, 100, 127, out var snap)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.IsClientBridge(snap)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.IsClientBridge(snap with { ToZone = 0 })).IsFalse();
    }

    [Test]
    public async Task AdvancedTime_MovesByTheSameDeltaAsPosition()
    {
        var last = Body(10000f, 8000f, BoatSeamHandoffRules.EncodeVelMetresPerSecond(10f));
        last.Time = 5000;
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            last, 10_000, null, 0, 1, 186, 218, 10_000, 250, 127, out var snap)).IsTrue();

        await Assert.That(BoatSeamHandoffRules.AdvancedTime(snap)).IsEqualTo(5250u);
        await Assert.That(BoatSeamHandoffRules.EvaluateTime(snap, 10_400)).IsEqualTo(5400u);
    }

    [Test]
    public async Task IsSafeProjectionZone_AllowsSourceOrDestinationOnly()
    {
        await Assert.That(BoatSeamHandoffRules.IsSafeProjectionZone(218, 186, 218)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.IsSafeProjectionZone(186, 186, 218)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.IsSafeProjectionZone(0, 186, 218)).IsFalse();
        await Assert.That(BoatSeamHandoffRules.IsSafeProjectionZone(257, 186, 218)).IsFalse();
    }

    [Test]
    public async Task TryBindActivation_ShrinksDeltaWhenTheProjectionLeavesBothZones()
    {
        var velX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(20f);
        await Assert.That(BoatSeamHandoffRules.TryCapture(
            Body(10000f, 8000f, velX), 10_000, null, 0, 1, 186, 218, 10_000, 1000, 127, out var snap))
            .IsTrue();

        uint ZoneAt(float x, float y) => x < 10010f ? 218u : 257u;

        await Assert.That(BoatSeamHandoffRules.TryBindActivationInDestinationZone(
            snap, 11_000, ZoneAt, out var bound)).IsTrue();
        await Assert.That(BoatSeamHandoffRules.DeltaMs(bound)).IsLessThan(1000);
        var (x, _, _, _, _, _) = BoatSeamHandoffRules.Propagate(bound);
        await Assert.That(x).IsLessThan(10010f);
    }
}
