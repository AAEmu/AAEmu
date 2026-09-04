using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatZoneKeyStabilityTests
{
    private const uint ZoneA = 149;
    private const uint ZoneB = 218;

    // The tracker registry is process-global static state and TUnit runs tests in parallel, so
    // every test drives its own hull id to stay deterministic under concurrency.
    private static int _nextHullId = 900_000;

    private static uint NewHull() => (uint)Interlocked.Increment(ref _nextHullId);

    [Test]
    public async Task Resolve_KeepsCurrentKeyUntilSampleIsStable()
    {
        var hull = NewHull();
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneB);
    }

    [Test]
    public async Task Resolve_ShortGrazeDoesNotFlipZone()
    {
        // Hull pokes across the seam for a couple of samples and comes back: the committed key
        // must stay on the home zone throughout, and home-zone samples count as evidence too
        // (they must not starve or reset the tally — that was part of the observed 17 s delay).
        var hull = NewHull();
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneA)).IsEqualTo(ZoneA);
    }

    [Test]
    public async Task Resolve_SeamedCrossingCommitsWithinPendingCap()
    {
        // Worst-case seam straddle: samples come back B,B,A over and over — no three consecutive
        // agreeing samples ever occur, but the majority candidate (B) must commit once
        // MaxPendingSamples have accrued instead of leaving the old zone in authority forever.
        // This is the regression for the observed 17-second handoff delay on a seam crossing.
        var hull = NewHull();

        // 11 samples of B,B,A…: still below the cap, so authority stays with the current zone.
        for (var i = 0; i < 3; i++)
        {
            await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
            await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
            await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneA)).IsEqualTo(ZoneA);
        }

        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);

        // 12th sample hits the cap: B leads the tally 9–3, so the hull hands off to B.
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneB);

        // Tracker was consumed by the commit: a fresh straddle starts over from the current key.
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneB)).IsEqualTo(ZoneB);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneB)).IsEqualTo(ZoneB);
    }

    [Test]
    public async Task Resolve_PerfectTieGoesToMostRecentlySampledKey()
    {
        // A perfectly even straddle sits exactly on the seam; the tie-break keeps the hull where
        // it currently is rather than teleporting authority to either side.
        var hull = NewHull();
        for (var i = 0; i < 6; i++)
        {
            await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
            await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneA)).IsEqualTo(ZoneA);
        }
    }

    [Test]
    public async Task Resolve_UnusableSamplesDoNotExtendTheLatencyBound()
    {
        // Zero keys (unsampled regions) must neither count toward the cap nor wipe pending
        // evidence, so sparse sampling cannot stretch the bound.
        var hull = NewHull();
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, 0, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, 0, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneB, ZoneA)).IsEqualTo(ZoneB);

        // Fresh tracker after that commit: a new candidate still needs its own run.
        var other = NewHull();
        await Assert.That(BoatZoneKeyStability.Resolve(other, ZoneA, ZoneB)).IsEqualTo(ZoneB);
        await Assert.That(BoatZoneKeyStability.Resolve(other, ZoneA, ZoneB)).IsEqualTo(ZoneB);
        await Assert.That(BoatZoneKeyStability.Resolve(other, ZoneA, ZoneB)).IsEqualTo(ZoneA);
    }

    [Test]
    public async Task ForceCommit_AcceptsSampleImmediately()
    {
        var hull = NewHull();
        await Assert.That(BoatZoneKeyStability.ForceCommit(hull, ZoneB)).IsEqualTo(ZoneB);
        await Assert.That(BoatZoneKeyStability.Resolve(hull, ZoneA, ZoneB)).IsEqualTo(ZoneB);
    }

    [Test]
    public async Task TrackersArePerHull()
    {
        var first = NewHull();
        var second = NewHull();

        await Assert.That(BoatZoneKeyStability.Resolve(first, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(second, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(first, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(second, ZoneA, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(first, ZoneB, ZoneA)).IsEqualTo(ZoneB);
        await Assert.That(BoatZoneKeyStability.Resolve(second, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(second, ZoneB, ZoneA)).IsEqualTo(ZoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(second, ZoneB, ZoneA)).IsEqualTo(ZoneB);
    }
}
