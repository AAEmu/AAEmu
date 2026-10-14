using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// The instance copy phase clock. Uses zone group 130's budget (ready 10 / play 1000 / end 60 s).
/// </summary>
public class IndunInstancePhaseRulesTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IndunZoneOption Hereafter = new(127, 10, 1000, 60);

    [Test]
    public async Task NoneBudget_IsNeverScripted()
    {
        await Assert.That(IndunInstancePhaseRules.PhaseAt(IndunZoneOption.None, T0, T0.AddHours(5)))
            .IsEqualTo(IndunInstancePhase.None);
        await Assert.That(IndunInstancePhaseRules.SecondsRemaining(IndunZoneOption.None, T0, T0))
            .IsEqualTo(0);
    }

    [Test]
    public async Task ReadyPhase_CountsDownFromReadyTime()
    {
        var s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0);
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Ready);
        await Assert.That(s.SecondsRemaining).IsEqualTo(10);

        s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(5));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Ready);
        await Assert.That(s.SecondsRemaining).IsEqualTo(5);
    }

    [Test]
    public async Task PlayPhase_StartsWhenReadyEnds()
    {
        var s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(10));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Play);
        await Assert.That(s.SecondsRemaining).IsEqualTo(1000);

        s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(1000));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Play);
        await Assert.That(s.SecondsRemaining).IsEqualTo(10);
    }

    [Test]
    public async Task EndPhase_ThenFinished()
    {
        var s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(1010));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.End);
        await Assert.That(s.SecondsRemaining).IsEqualTo(60);

        s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(1069));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.End);
        await Assert.That(s.SecondsRemaining).IsEqualTo(1);

        s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(1070));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Finished);
        await Assert.That(s.SecondsRemaining).IsEqualTo(0);
    }

    [Test]
    public async Task ZeroReady_SkipsStraightToPlay()
    {
        var option = new IndunZoneOption(0, 0, 100, 0);

        var s = IndunInstancePhaseRules.StateAt(option, T0, T0);
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Play);
        await Assert.That(s.SecondsRemaining).IsEqualTo(100);
    }

    [Test]
    public async Task NowBeforeStart_ClampsToReady()
    {
        var s = IndunInstancePhaseRules.StateAt(Hereafter, T0, T0.AddSeconds(-5));
        await Assert.That(s.Phase).IsEqualTo(IndunInstancePhase.Ready);
        await Assert.That(s.SecondsRemaining).IsEqualTo(10);
    }

    [Test]
    public async Task ScriptStart_NeedsATowerDef()
    {
        var noTower = new IndunZoneOption(0, 10, 1000, 60);
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                noTower, contentLoaded: true, alreadyStarted: false, T0, T0.AddSeconds(60)))
            .IsFalse();
    }

    [Test]
    public async Task ScriptStart_WaitsOutTheReadyWindow()
    {
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                Hereafter, contentLoaded: true, alreadyStarted: false, T0, T0))
            .IsFalse();
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                Hereafter, contentLoaded: true, alreadyStarted: false, T0, T0.AddSeconds(9)))
            .IsFalse();
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                Hereafter, contentLoaded: true, alreadyStarted: false, T0, T0.AddSeconds(10)))
            .IsTrue();
    }

    [Test]
    public async Task ScriptStart_NeedsLoadedContentAndOnlyOnce()
    {
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                Hereafter, contentLoaded: false, alreadyStarted: false, T0, T0.AddSeconds(30)))
            .IsFalse();
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                Hereafter, contentLoaded: true, alreadyStarted: true, T0, T0.AddSeconds(30)))
            .IsFalse();
    }

    [Test]
    public async Task ScriptStart_NoBudget_StartsAtOnce()
    {
        var noBudget = new IndunZoneOption(127, 0, 0, 0);
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                noBudget, contentLoaded: true, alreadyStarted: false, T0, T0))
            .IsTrue();
    }

    [Test]
    public async Task ScriptStart_AfterTheBudget_DoesNotStart()
    {
        await Assert.That(IndunInstancePhaseRules.ShouldStartScript(
                Hereafter, contentLoaded: true, alreadyStarted: false, T0, T0.AddSeconds(1070)))
            .IsFalse();
    }

    [Test]
    public async Task ClockOrigin_IsTheFirstArrival()
    {
        await Assert.That(IndunInstancePhaseRules.TryGetClockOrigin(T0, out var origin)).IsTrue();
        await Assert.That(origin).IsEqualTo(T0);
    }

    [Test]
    public async Task ClockOrigin_AnEmptyCopyHasNoClock()
    {
        await Assert.That(IndunInstancePhaseRules.TryGetClockOrigin(null, out var origin)).IsFalse();
        await Assert.That(origin).IsEqualTo(default(DateTime));
    }

    [Test]
    public async Task ClockOrigin_SetOnArrival_FullBudgetIsStillAhead()
    {
        // The arrival is the origin: nothing of the budget is spent before it, so an invite that sat
        // unaccepted for an hour still starts its ready window with a full 10 s left.
        var arrival = T0.AddHours(1);
        await Assert.That(IndunInstancePhaseRules.TryGetClockOrigin(arrival, out var origin)).IsTrue();
        var state = IndunInstancePhaseRules.StateAt(Hereafter, origin, arrival);
        await Assert.That(state.Phase).IsEqualTo(IndunInstancePhase.Ready);
        await Assert.That(state.SecondsRemaining).IsEqualTo(10);
    }
}
