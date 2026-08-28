using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class HullSpeedMonitorTests
{
    private static uint _next = 9000;
    private static uint NextHull() => _next++;

    /// <summary>The simulating zone; measurement restarts when a hull is handed to another one.</summary>
    private const uint Zone = 186;
    private const uint OtherZone = 218;

    [Test]
    public async Task Observe_NeedsTwoSamples()
    {
        var hull = NextHull();

        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 0f, 1000)).IsNull();
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 10f, 0f, 0f, 2000)).IsEqualTo(10f);

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_IgnoresSamplesTooCloseTogether()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 0f, 1000);

        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 1f, 0f, 0f, 1000 + HullSpeedMonitor.MinSampleMs - 1))
            .IsNull();

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_IgnoresSamplesTooFarApart()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 0f, 1000);

        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 2f, 0f, 0f, 1000 + HullSpeedMonitor.MaxSampleMs + 1))
            .IsNull();

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_KeepsTheBaselineWhenASampleIsRejected()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 0f, 1000);
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 1f, 0f, 0f, 1010)).IsNull();

        // Measured from the kept baseline, not from the rejected sample: eleven metres since 1000.
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 11f, 0f, 0f, 2000)).IsEqualTo(11f);

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_MeasuresWhenReportsArriveFasterThanTheSampleFloor()
    {
        var hull = NextHull();

        // A hull making ten metres a second, published every 50 ms — half the sample floor, which is the
        // cadence a simulator actually reports at. Holding the baseline is what lets the third report
        // measure; replacing it on every report leaves every pair one interval apart and never measures.
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 0.0f, 0f, 0f, 1000)).IsNull();
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 0.5f, 0f, 0f, 1050)).IsNull();
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 1.0f, 0f, 0f, 1100)).IsEqualTo(10f);

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_MeasuresTheDistanceTravelled()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 100f, 100f, 10f, 5000);

        // 3-4-5: five metres across half a second.
        var speed = HullSpeedMonitor.Observe(hull, Zone, 103f, 104f, 10f, 5500);

        await Assert.That(speed).IsEqualTo(10f);

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_RestartsWhenTheHullChangesSimulator()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 100f, 1000);

        // The first report from the new simulator is a fresh baseline, not travel: the two zones do not
        // agree to the metre, and measuring across the switch reports that step as speed.
        await Assert.That(HullSpeedMonitor.Observe(hull, OtherZone, 40f, 0f, 100f, 1500)).IsNull();

        // Measurement resumes within the new simulator alone.
        await Assert.That(HullSpeedMonitor.Observe(hull, OtherZone, 41f, 0f, 100f, 1600)).IsEqualTo(10f);

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_IgnoresVerticalMotion()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 100f, 1000);

        // A hull riding a swell gets nowhere: three metres up in half a second is not three metres made
        // good, and counting it would inflate every speed a hull on open water reports.
        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 103f, 1500)).IsEqualTo(0f);

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task Observe_ForgottenHullStartsOver()
    {
        var hull = NextHull();
        HullSpeedMonitor.Observe(hull, Zone, 0f, 0f, 0f, 1000);
        HullSpeedMonitor.Forget(hull);

        await Assert.That(HullSpeedMonitor.Observe(hull, Zone, 10f, 0f, 0f, 2000)).IsNull();

        HullSpeedMonitor.Forget(hull);
    }

    [Test]
    public async Task IsOverspeed_AllowsTheHullsOwnMaximum()
    {
        await Assert.That(HullSpeedMonitor.IsOverspeed(12.6f, 12.6f)).IsFalse();
        await Assert.That(HullSpeedMonitor.IsOverspeed(15f, 12.6f)).IsFalse();
        await Assert.That(HullSpeedMonitor.IsOverspeed(31f, 12.6f)).IsTrue();
    }

    [Test]
    public async Task IsOverspeed_SaysNothingWithoutAShipModel()
    {
        await Assert.That(HullSpeedMonitor.IsOverspeed(31f, 0f)).IsFalse();
    }

    [Test]
    public async Task ShouldReport_RateLimitsPerHull()
    {
        var hull = NextHull();
        var other = NextHull();

        await Assert.That(HullSpeedMonitor.ShouldReport(hull, 10_000)).IsTrue();
        await Assert.That(HullSpeedMonitor.ShouldReport(hull, 10_500)).IsFalse();
        await Assert.That(HullSpeedMonitor.ShouldReport(other, 10_500)).IsTrue();
        await Assert.That(HullSpeedMonitor.ShouldReport(hull, 13_000)).IsTrue();

        HullSpeedMonitor.Forget(hull);
        HullSpeedMonitor.Forget(other);
    }
}
