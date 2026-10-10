using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// How an <c>indun_event_npc_info_broadcastings</c> row becomes an <c>npcInfo</c> row: type 1 a buff stack
/// count (no bound), type 2 the buff's remaining milliseconds and whole duration. An unknown type is
/// skipped rather than sent as a wrong number.
/// </summary>
/// <remarks>
/// The time form travels in milliseconds: the client's readout divides by 1000 itself before display
/// (<c>indun_playing_info</c> does <c>data.time = v2.leftTime / 1000</c>), so whole seconds read as ~0.
/// </remarks>
public class IndunPlayingInfoRulesTests
{
    [Test]
    public async Task StackCount_ReportsTheCountAndNoBound()
    {
        await Assert.That(IndunPlayingInfoRules.TryReadValues(
            IndunPlayingInfoRules.BroadcastingStackCount, stackCount: 7, remainingMs: 0, durationMs: 0,
            out var value, out var limit)).IsTrue();
        await Assert.That(value).IsEqualTo(7u);
        await Assert.That(limit).IsEqualTo(0u);
    }

    [Test]
    public async Task RemainingTime_ReportsMillisecondsLeftAndWholeDuration()
    {
        await Assert.That(IndunPlayingInfoRules.TryReadValues(
            IndunPlayingInfoRules.BroadcastingRemainingTime, stackCount: 0, remainingMs: 90_400, durationMs: 600_000,
            out var value, out var limit)).IsTrue();
        await Assert.That(value).IsEqualTo(90_400u);
        await Assert.That(limit).IsEqualTo(600_000u);
    }

    [Test]
    public async Task UnknownBroadcastingType_ReportsNothing()
    {
        await Assert.That(IndunPlayingInfoRules.TryReadValues(
            99, stackCount: 1, remainingMs: 1000, durationMs: 1000, out var value, out var limit)).IsFalse();
        await Assert.That(value).IsEqualTo(0u);
        await Assert.That(limit).IsEqualTo(0u);
    }

    [Test]
    public async Task AbsentBuff_StackCounterReadsZero()
    {
        await Assert.That(IndunPlayingInfoRules.TryReadAbsentBuff(
            IndunPlayingInfoRules.BroadcastingStackCount, out var value, out var limit)).IsTrue();
        await Assert.That(value).IsEqualTo(0u);
        await Assert.That(limit).IsEqualTo(0u);
    }

    [Test]
    public async Task AbsentBuff_TimeReadoutReadsZero()
    {
        // Before the script puts the buff up the HUD row shows zero rather than disappearing.
        await Assert.That(IndunPlayingInfoRules.TryReadAbsentBuff(
            IndunPlayingInfoRules.BroadcastingRemainingTime, out var value, out var limit)).IsTrue();
        await Assert.That(value).IsEqualTo(0u);
        await Assert.That(limit).IsEqualTo(0u);
        await Assert.That(IndunPlayingInfoRules.TryReadAbsentBuff(99, out _, out _)).IsFalse();
    }

    [Test]
    public async Task Shape_ChangesEveryDisplayedSecondOfARunningCountdown()
    {
        // The client shows the last reading as-is, so the next displayed second needs a new packet.
        var early = new[] { new IndunPlayingInfoNpc(1, 2, 2, 900_000, 960_000) };
        var later = new[] { new IndunPlayingInfoNpc(1, 2, 2, 899_000, 960_000) };
        await Assert.That(IndunPlayingInfoRules.Shape(later)).IsNotEqualTo(IndunPlayingInfoRules.Shape(early));
    }

    [Test]
    public async Task Shape_IgnoresMovementWithinTheSameSecond()
    {
        var early = new[] { new IndunPlayingInfoNpc(1, 2, 2, 899_900, 960_000) };
        var later = new[] { new IndunPlayingInfoNpc(1, 2, 2, 899_100, 960_000) };
        await Assert.That(IndunPlayingInfoRules.Shape(later)).IsEqualTo(IndunPlayingInfoRules.Shape(early));
    }

    [Test]
    public async Task Shape_ChangesWhenATimerStartsOrACountMoves()
    {
        var absent = new[] { new IndunPlayingInfoNpc(1, 2, 1, 0, 0), new IndunPlayingInfoNpc(1, 3, 1, 0, 0) };
        var timerUp = new[] { new IndunPlayingInfoNpc(1, 2, 2, 960_000, 960_000), new IndunPlayingInfoNpc(1, 3, 1, 0, 0) };
        var counted = new[] { new IndunPlayingInfoNpc(1, 2, 2, 960_000, 960_000), new IndunPlayingInfoNpc(1, 3, 1, 1, 0) };
        await Assert.That(IndunPlayingInfoRules.Shape(timerUp)).IsNotEqualTo(IndunPlayingInfoRules.Shape(absent));
        await Assert.That(IndunPlayingInfoRules.Shape(counted)).IsNotEqualTo(IndunPlayingInfoRules.Shape(timerUp));
    }

    [Test]
    public async Task NegativeReadings_ClampToZero()
    {
        // An expired buff reports -1 ms left; the wire carries an unsigned reading, never a wrapped u32.
        await Assert.That(IndunPlayingInfoRules.TryReadValues(
            IndunPlayingInfoRules.BroadcastingRemainingTime, stackCount: 0, remainingMs: -1, durationMs: -5,
            out var value, out var limit)).IsTrue();
        await Assert.That(value).IsEqualTo(0u);
        await Assert.That(limit).IsEqualTo(0u);
    }
}
