using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// The round-timer fields of SCIndunUpdateRoundInfoPacket (0x2DA). A round with an authored
/// <c>indun_rounds.timer</c> reports its limit and the seconds played; one without reports no time limit,
/// so the client draws no countdown for it.
/// </summary>
public class IndunRoundTimerWireTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RoundWithTimer_ReportsLimitAndElapsedPlayTime()
    {
        var (limit, play, isTimeLimit) = IndunRoundRules.RoundTimerWire(T0, 120, T0.AddSeconds(30));
        await Assert.That(limit).IsEqualTo(120u);
        await Assert.That(play).IsEqualTo(30u);
        await Assert.That(isTimeLimit).IsTrue();
    }

    [Test]
    public async Task RoundWithoutAnAuthoredTimer_HasNoTimeLimit()
    {
        var (limit, play, isTimeLimit) = IndunRoundRules.RoundTimerWire(T0, 0, T0.AddSeconds(30));
        await Assert.That(limit).IsEqualTo(0u);
        await Assert.That(play).IsEqualTo(0u);
        await Assert.That(isTimeLimit).IsFalse();
    }

    [Test]
    public async Task RoundThatNeverStarted_HasNoTimeLimit()
    {
        var (_, _, isTimeLimit) = IndunRoundRules.RoundTimerWire(null, 120, T0);
        await Assert.That(isTimeLimit).IsFalse();
    }

    [Test]
    public async Task PlayTime_ClampsToTheLimit()
    {
        // The timer ran out; the wire value never exceeds the limit.
        var (limit, play, _) = IndunRoundRules.RoundTimerWire(T0, 120, T0.AddSeconds(900));
        await Assert.That(limit).IsEqualTo(120u);
        await Assert.That(play).IsEqualTo(120u);
    }

    [Test]
    public async Task PlayTime_NeverWrapsBelowZero()
    {
        // Clock skew (a "now" before the start) reports zero, not a wrapped u32.
        var (_, play, _) = IndunRoundRules.RoundTimerWire(T0, 120, T0.AddSeconds(-5));
        await Assert.That(play).IsEqualTo(0u);
    }
}
