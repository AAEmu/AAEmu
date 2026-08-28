using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatSeamImpulseTests
{
    private const float Delta = 0.0001f;

    [Test]
    public async Task Disabled_KillSwitchOff_RestoresNothing()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(false, 10f, 0, 100, out var speed)).IsFalse();
        await Assert.That(speed).IsEqualTo(0f);
    }

    [Test]
    public async Task BarelyMoving_KeepsRestSeed()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, BoatSeamImpulse.MinCruiseSpeed - Delta, 0, 100, out _))
            .IsFalse();
    }

    [Test]
    public async Task StaleMeasurement_KeepsRestSeed()
    {
        // A figure older than the freshness window describes a hull that has since stopped or docked;
        // restoring it would launch a parked ship.
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, 10f, BoatSeamImpulse.FreshnessWindowMs + 1, 100, out _))
            .IsFalse();
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, 10f, long.MaxValue / 2, 100, out _))
            .IsFalse();
    }

    [Test]
    public async Task HelmAtZero_RiderChoseToStop_KeepsRestSeed()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, 10f, 0, 0, out _)).IsFalse();
    }

    [Test]
    public async Task AboveTheModelFigure_StillRestored()
    {
        // The model figure is a thrust cut-off, not a limit: a rigged hull sails past it on wind stacks
        // and momentum, and the fast crossings are exactly the ones that need re-driving. Refusing them
        // as "corrupt" was what left those seams feeling like a wall.
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, 20.3f, 0, 127, out var speed)).IsTrue();
        await Assert.That(speed).IsEqualTo(20.3f * BoatSeamImpulse.RestoreFactor);
    }

    [Test]
    public async Task UnderWay_ContinuesMeasuredSpeedOnForwardAxis()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, 10f, 100, 100, out var speed)).IsTrue();
        await Assert.That(speed).IsEqualTo(10f * BoatSeamImpulse.RestoreFactor);

        var vel = new float[3];
        var angVel = new float[3];
        var impulse = new float[3];
        var angImpulse = new float[3];
        BoatSeamImpulse.BuildVectors(speed, vel, angVel, impulse, angImpulse);

        // Authored drive rows put their magnitude on local +Y; the zone's self-cast rotation turns that
        // axis into the bow. Every other channel must stay zero.
        await Assert.That(vel[1]).IsGreaterThan(0f);
        await Assert.That(vel[0]).IsEqualTo(0f);
        await Assert.That(vel[2]).IsEqualTo(0f);
        foreach (var v in angVel.Concat(impulse).Concat(angImpulse))
            await Assert.That(v).IsEqualTo(0f);
    }

    [Test]
    public async Task ExtremeMeasurement_IsCapped()
    {
        // The sanity ceiling still applies, for a saturated reading rather than a fast hull.
        await Assert.That(
            BoatSeamImpulse.TryBuildForwardVelocity(true, 500f, 0, 100, out var speed)).IsTrue();
        await Assert.That(speed).IsEqualTo(BoatSeamImpulse.MaxRestoredSpeed);
    }

    /// <summary>A cut-off comfortably above the speeds used here, so it never interferes.</summary>
    private const float AmpleCutoff = 100f;

    [Test]
    public async Task SeamCorrection_SendsExactlyTheShortfall()
    {
        // The impulse channel is additive, so the gap between what the hull arrived with and what it was
        // carrying is precisely what has to be sent — no more, or it overshoots.
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 13.5f, 8.1f, AmpleCutoff, out var deficit)).IsTrue();
        await Assert.That(deficit).IsEqualTo(13.5f - 8.1f).Within(0.001f);
    }

    [Test]
    public async Task SeamCorrection_LeavesASmallGapAlone()
    {
        // Inside this band the thrust curve closes the gap on its own; correcting chases quantisation noise.
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(
                true, 10f, 10f - (BoatSeamImpulse.MinCorrectionDeficit / 2f), AmpleCutoff, out _)).IsFalse();
    }

    [Test]
    public async Task SeamCorrection_NeverPushesAHullThatArrivedFasterThanItLeft()
    {
        // A negative shortfall must not become a brake, and must not be sent as a positive impulse either.
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 10f, 14f, AmpleCutoff, out var deficit)).IsFalse();
        await Assert.That(deficit).IsEqualTo(0f);
    }

    [Test]
    public async Task SeamCorrection_IgnoresAHullThatWasBarelyMoving()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(
                true, BoatSeamImpulse.MinCruiseSpeed - Delta, 0f, AmpleCutoff, out _)).IsFalse();
    }

    [Test]
    public async Task SeamCorrection_KillSwitchOff_CorrectsNothing()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(false, 13.5f, 8.1f, AmpleCutoff, out var deficit)).IsFalse();
        await Assert.That(deficit).IsEqualTo(0f);
    }

    [Test]
    public async Task SeamCorrection_IsCappedForASaturatedReading()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(
                true, 500f, BoatSeamImpulse.MinCruiseSpeed, 0f, out var deficit)).IsTrue();
        await Assert.That(deficit).IsEqualTo(BoatSeamImpulse.MaxRestoredSpeed);
    }

    [Test]
    public async Task SeamCorrection_IgnoresAnInterpolationSample()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 18.8f, 0f, 32.8f, out var deficit)).IsFalse();
        await Assert.That(deficit).IsEqualTo(0f);
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 18.8f, 0.2f, 32.8f, out deficit)).IsFalse();
        await Assert.That(deficit).IsEqualTo(0f);
    }

    [Test]
    public async Task SeamCorrection_StandsDownForACoastingHull()
    {
        // Measured live: a hull whose cut-off had collapsed to 6 m/s was being held at 21 by a correction
        // on every crossing. Above the cut-off the engine contributes nothing, so re-driving it each seam
        // ratchets the hull to a speed its own physics never supports.
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 21.0f, 11.4f, 6.0f, out var deficit)).IsFalse();
        await Assert.That(deficit).IsEqualTo(0f);
    }

    [Test]
    public async Task SeamCorrection_StillActsAtExactlyTheCutoff()
    {
        // At the cut-off the hull is still under power; only beyond it is it coasting.
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 12.6f, 8.0f, 12.6f, out var deficit)).IsTrue();
        await Assert.That(deficit).IsEqualTo(12.6f - 8.0f).Within(0.001f);
    }

    [Test]
    public async Task SeamCorrection_UnknownCutoffDoesNotBlockTheCorrection()
    {
        await Assert.That(
            BoatSeamImpulse.TryBuildSeamCorrection(true, 13.5f, 8.1f, 0f, out var deficit)).IsTrue();
        await Assert.That(deficit).IsEqualTo(13.5f - 8.1f).Within(0.001f);
    }
}
