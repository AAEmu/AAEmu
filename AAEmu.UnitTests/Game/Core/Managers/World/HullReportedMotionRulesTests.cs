using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class HullReportedMotionRulesTests
{
    /// <summary>Fresh enough for the report to be checked against the measurement.</summary>
    private const long Fresh = 200;

    [Test]
    public async Task FrozenHullReportingWay_IsNotBelieved()
    {
        // The Ostera reading: the velocity field still holds the speed the hull last made while the
        // travelled distance is three orders of magnitude below it.
        await Assert.That(
            HullReportedMotionRules.IsReportedMotionCorroborated(2.4f, 0.0005f, Fresh))
            .IsFalse();
    }

    [Test]
    public async Task TravellingHull_IsBelieved()
    {
        await Assert.That(HullReportedMotionRules.IsReportedMotionCorroborated(8f, 7.6f, Fresh)).IsTrue();
    }

    [Test]
    public async Task AcceleratingHull_MeasuresLowerThanItReports_StillBelieved()
    {
        // A window average over a uniform ramp is about half the end speed, so the honest worst case
        // must stay above the threshold or every departure would be seeded at rest.
        await Assert.That(HullReportedMotionRules.IsReportedMotionCorroborated(8f, 4f, Fresh)).IsTrue();
        await Assert.That(
            HullReportedMotionRules.IsReportedMotionCorroborated(
                8f, 8f * HullReportedMotionRules.MinCorroboratedFraction, Fresh))
            .IsTrue();
    }

    [Test]
    public async Task StaleOrAbsentMeasurement_CorroboratesNothing_ReportKept()
    {
        // Missing evidence must not hold a hull back: a fast hull whose measurement lapsed still
        // crosses a seam with its way.
        await Assert.That(
            HullReportedMotionRules.IsReportedMotionCorroborated(
                8f, 0f, HullReportedMotionRules.FreshnessWindowMs))
            .IsTrue();
        await Assert.That(
            HullReportedMotionRules.IsReportedMotionCorroborated(8f, 0f, long.MaxValue / 2)).IsTrue();
        await Assert.That(HullReportedMotionRules.IsReportedMotionCorroborated(8f, 0f, -1)).IsTrue();
    }

    [Test]
    public async Task ReportOfRest_HasNothingToReject()
    {
        await Assert.That(HullReportedMotionRules.IsReportedMotionCorroborated(0f, 0f, Fresh)).IsTrue();
    }

    [Test]
    public async Task NonFiniteFigures_AreNotTreatedAsMotion()
    {
        await Assert.That(
            HullReportedMotionRules.IsReportedMotionCorroborated(float.NaN, 0f, Fresh)).IsTrue();
        await Assert.That(
            HullReportedMotionRules.IsReportedMotionCorroborated(8f, float.NaN, Fresh)).IsTrue();
    }
}
