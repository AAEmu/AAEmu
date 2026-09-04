using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotChannelingRulesTests
{
    [Test]
    public async Task SportFishChannelStart_SplitsCastFromChannel()
    {
        // Plot 821 event 6859: 1500 ms was the previous (cast) edge; this node starts
        // a 220 s channel plus a 19.5 s bite-roll. Those must not share one field.
        var (castingMs, channelingMs) = PlotChannelingRules.NextEdgeDurations(
        [
            (Casting: false, Channeling: true, DelayMs: 220_000),
            (Casting: false, Channeling: false, DelayMs: 19_500)
        ]);

        await Assert.That(castingMs).IsEqualTo(0);
        await Assert.That(channelingMs).IsEqualTo(220_000);
        await Assert.That(PlotChannelingRules.ToPlotWireTime(channelingMs)).IsEqualTo((ushort)22_000);
    }

    [Test]
    public async Task CastEdge_DoesNotCountAsChannel()
    {
        var (castingMs, channelingMs) = PlotChannelingRules.NextEdgeDurations(
        [
            (Casting: true, Channeling: false, DelayMs: 1500)
        ]);

        await Assert.That(castingMs).IsEqualTo(1500);
        await Assert.That(channelingMs).IsEqualTo(0);
        await Assert.That(PlotChannelingRules.ToPlotWireTime(castingMs)).IsEqualTo((ushort)150);
    }

    [Test]
    public async Task BiteOrCancel_ResumesTheChannelWait_NotTheRollLoop()
    {
        var queued = new[] { "roll-loop", "channel-end", "roll-loop-again" };
        var index = PlotChannelingRules.IndexOfChannelWait(
            queued,
            id => id == "channel-end");

        await Assert.That(index).IsEqualTo(1);
    }

    [Test]
    public async Task NoChannelWait_ReturnsMissing()
    {
        var queued = new[] { "roll-loop" };
        var index = PlotChannelingRules.IndexOfChannelWait(queued, _ => false);
        await Assert.That(index).IsEqualTo(-1);
    }

    [Test]
    public async Task BaitCastEdge_WireIncludesAnimCsTime()
    {
        // Plot 809 6752→6753: delay 1500, add_anim_cs_time, fist_fishing_casting sync.
        var scheduled = PlotChannelingRules.IncludeAnimCsTime(1500, addAnimCsTime: true, animCsTimeMs: 1500);
        await Assert.That(scheduled).IsEqualTo(3000);
        await Assert.That(PlotChannelingRules.ToPlotWireTime(scheduled)).IsEqualTo((ushort)300);
        await Assert.That(PlotChannelingRules.IncludeAnimCsTime(1500, addAnimCsTime: false, animCsTimeMs: 1500))
            .IsEqualTo(1500);
        await Assert.That(PlotChannelingRules.IncludeAnimCsTime(1500, addAnimCsTime: true, animCsTimeMs: 0))
            .IsEqualTo(1500);
    }

    [Test]
    public async Task IgnoredStop_RefreshesOncePerInterval()
    {
        var t0 = new DateTime(2026, 9, 4, 23, 6, 31, DateTimeKind.Utc);
        await Assert.That(PlotChannelingRules.ShouldRefreshPlotAfterIgnoredStop(false, default, t0))
            .IsFalse();
        await Assert.That(PlotChannelingRules.ShouldRefreshPlotAfterIgnoredStop(true, default, t0))
            .IsTrue();
        await Assert.That(
                PlotChannelingRules.ShouldRefreshPlotAfterIgnoredStop(
                    true,
                    t0,
                    t0.AddMilliseconds(PlotChannelingRules.IgnoredStopRefreshMinMs - 1)))
            .IsFalse();
        await Assert.That(
                PlotChannelingRules.ShouldRefreshPlotAfterIgnoredStop(
                    true,
                    t0,
                    t0.AddMilliseconds(PlotChannelingRules.IgnoredStopRefreshMinMs)))
            .IsTrue();
    }
}
