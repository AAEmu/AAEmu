using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunRoundRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Zone group 125 shape: round 1 and every fifth round carry a 120 s timer, the fifth ones are boss rounds.</summary>
    private static List<IndunRound> ChallengeTower(int total = 50)
    {
        var rounds = new List<IndunRound>();
        for (var i = 1; i <= total; i++)
        {
            var fifth = i % 5 == 0;
            rounds.Add(new IndunRound
            {
                Id = (uint)i,
                ZoneGroupId = 125,
                Round = i,
                SpawnerId = 188246u + (uint)i,
                TimerSeconds = i == 1 || fifth ? 120 : 0,
                BossRound = fifth
            });
        }

        return rounds;
    }

    [Test]
    public async Task NextRound_AddsRoundAdd()
    {
        await Assert.That(IndunRoundRules.NextRound(0, 50, 1)).IsEqualTo(1);
        await Assert.That(IndunRoundRules.NextRound(1, 50, 4)).IsEqualTo(5);
        await Assert.That(IndunRoundRules.NextRound(5, 50, 5)).IsEqualTo(10);
    }

    [Test]
    public async Task NextRound_ClampsAtTheLastRound()
    {
        await Assert.That(IndunRoundRules.NextRound(46, 50, 5)).IsEqualTo(50);
        await Assert.That(IndunRoundRules.NextRound(50, 50, 1)).IsEqualTo(50);
    }

    [Test]
    public async Task NextRound_ZeroAddKeepsTheRound()
    {
        await Assert.That(IndunRoundRules.NextRound(21, 21, 0)).IsEqualTo(21);
        await Assert.That(IndunRoundRules.NextRound(7, 21, 0)).IsEqualTo(7);
    }

    [Test]
    public async Task NextRound_WithoutRoundsIsNeutral()
    {
        await Assert.That(IndunRoundRules.NextRound(0, 0, 1)).IsEqualTo(0);
        await Assert.That(IndunRoundRules.NextRound(3, 0, 5)).IsEqualTo(3);
    }

    [Test]
    public async Task IsCompletion_OnTheLastRoundWithAnyAdd()
    {
        await Assert.That(IndunRoundRules.IsCompletion(50, 50, 1)).IsTrue();
        await Assert.That(IndunRoundRules.IsCompletion(50, 50, 5)).IsTrue();
    }

    [Test]
    public async Task IsCompletion_OnTheExplicitZeroAdd()
    {
        // action 327 "last round clear": round_add 0 while still inside the run.
        await Assert.That(IndunRoundRules.IsCompletion(20, 21, 0)).IsTrue();
    }

    [Test]
    public async Task IsCompletion_NeverBeforeTheFirstRound()
    {
        await Assert.That(IndunRoundRules.IsCompletion(0, 21, 0)).IsFalse();
        await Assert.That(IndunRoundRules.IsCompletion(0, 50, 1)).IsFalse();
    }

    [Test]
    public async Task IsCompletion_NotMidRun()
    {
        await Assert.That(IndunRoundRules.IsCompletion(3, 50, 1)).IsFalse();
        await Assert.That(IndunRoundRules.IsCompletion(49, 50, 1)).IsFalse();
    }

    [Test]
    public async Task IsCompletion_WithoutRoundsIsNeutral()
    {
        await Assert.That(IndunRoundRules.IsCompletion(0, 0, 0)).IsFalse();
        await Assert.That(IndunRoundRules.IsCompletion(1, 0, 0)).IsFalse();
    }

    [Test]
    public async Task ShouldGrantCompletion_OnlyTheFirstTime()
    {
        await Assert.That(IndunRoundRules.ShouldGrantCompletion(alreadyCompleted: false)).IsTrue();
        await Assert.That(IndunRoundRules.ShouldGrantCompletion(alreadyCompleted: true)).IsFalse();
    }

    [Test]
    public async Task IsTimerRunning_InsideTheWindow()
    {
        await Assert.That(IndunRoundRules.IsTimerRunning(T0, 120, T0.AddSeconds(119))).IsTrue();
    }

    [Test]
    public async Task IsTimerRunning_StopsAtTheWindowEnd()
    {
        await Assert.That(IndunRoundRules.IsTimerRunning(T0, 120, T0.AddSeconds(120))).IsFalse();
        await Assert.That(IndunRoundRules.IsTimerRunning(T0, 120, T0.AddSeconds(300))).IsFalse();
    }

    [Test]
    public async Task IsTimerRunning_NeverWithoutTimerOrStart()
    {
        await Assert.That(IndunRoundRules.IsTimerRunning(T0, 0, T0.AddSeconds(1))).IsFalse();
        await Assert.That(IndunRoundRules.IsTimerRunning(null, 120, T0)).IsFalse();
    }

    [Test]
    public async Task PhaseCheckMatches_AlwaysIgnoresTheTimer()
    {
        await Assert.That(IndunRoundRules.PhaseCheckMatches(IndunRoundRules.CheckStatusAlways, true)).IsTrue();
        await Assert.That(IndunRoundRules.PhaseCheckMatches(IndunRoundRules.CheckStatusAlways, false)).IsTrue();
    }

    [Test]
    public async Task PhaseCheckMatches_TimerNeedsARunningTimer()
    {
        await Assert.That(IndunRoundRules.PhaseCheckMatches(IndunRoundRules.CheckStatusTimer, true)).IsTrue();
        await Assert.That(IndunRoundRules.PhaseCheckMatches(IndunRoundRules.CheckStatusTimer, false)).IsFalse();
    }

    [Test]
    public async Task PhaseCheckMatches_NoTimerNeedsAnExpiredOrAbsentTimer()
    {
        await Assert.That(IndunRoundRules.PhaseCheckMatches(IndunRoundRules.CheckStatusNoTimer, false)).IsTrue();
        await Assert.That(IndunRoundRules.PhaseCheckMatches(IndunRoundRules.CheckStatusNoTimer, true)).IsFalse();
    }

    [Test]
    public async Task PhaseCheckMatches_UnknownStatusFailsClosed()
    {
        await Assert.That(IndunRoundRules.PhaseCheckMatches(0, true)).IsFalse();
        await Assert.That(IndunRoundRules.PhaseCheckMatches(4, false)).IsFalse();
    }

    [Test]
    public async Task NextRoundIsBoss_ReadsTheFollowingRow()
    {
        var rounds = ChallengeTower();
        await Assert.That(IndunRoundRules.NextRoundIsBoss(rounds, 4)).IsTrue();
        await Assert.That(IndunRoundRules.NextRoundIsBoss(rounds, 5)).IsFalse();
        await Assert.That(IndunRoundRules.NextRoundIsBoss(rounds, 0)).IsFalse();
    }

    [Test]
    public async Task NextRoundIsBoss_FalsePastTheEndOrWithoutRows()
    {
        await Assert.That(IndunRoundRules.NextRoundIsBoss(ChallengeTower(), 50)).IsFalse();
        await Assert.That(IndunRoundRules.NextRoundIsBoss([], 0)).IsFalse();
        await Assert.That(IndunRoundRules.NextRoundIsBoss(null, 0)).IsFalse();
    }

    [Test]
    public async Task ToWireRound_ClampsToOneSignedByte()
    {
        await Assert.That(IndunRoundRules.ToWireRound(50)).IsEqualTo((sbyte)50);
        await Assert.That(IndunRoundRules.ToWireRound(-1)).IsEqualTo((sbyte)0);
        await Assert.That(IndunRoundRules.ToWireRound(500)).IsEqualTo(sbyte.MaxValue);
    }

    [Test]
    public async Task EndAlarmSuccess_FollowsTheNextRoundFlag()
    {
        await Assert.That(IndunRoundRules.EndAlarmSuccess(true)).IsTrue();
        await Assert.That(IndunRoundRules.EndAlarmSuccess(false)).IsFalse();
    }

    [Test]
    public async Task ResolveSpawnerId_EffectSpawnerWins()
    {
        await Assert.That(IndunRoundRules.ResolveSpawnerId(198386, 188247)).IsEqualTo(198386u);
    }

    [Test]
    public async Task ResolveSpawnerId_ZeroFallsBackToTheRound()
    {
        await Assert.That(IndunRoundRules.ResolveSpawnerId(0, 188247)).IsEqualTo(188247u);
        await Assert.That(IndunRoundRules.ResolveSpawnerId(0, 0)).IsEqualTo(0u);
    }

    [Test]
    public async Task HasRounds_OnlyForAPositiveTotal()
    {
        await Assert.That(IndunRoundRules.HasRounds(50)).IsTrue();
        await Assert.That(IndunRoundRules.HasRounds(0)).IsFalse();
    }

    [Test]
    public async Task State_TotalRoundsIsTheHighestRow()
    {
        await Assert.That(new IndunRoundState(ChallengeTower()).TotalRounds).IsEqualTo(50);
        await Assert.That(new IndunRoundState(ChallengeTower(21)).TotalRounds).IsEqualTo(21);
    }

    [Test]
    public async Task State_WithoutRowsIsInert()
    {
        var state = new IndunRoundState([]);
        await Assert.That(state.TotalRounds).IsEqualTo(0);
        await Assert.That(state.HasRounds).IsFalse();
        await Assert.That(state.ApplyNextRound(1)).IsFalse();
        await Assert.That(state.CurrentRound).IsEqualTo(0);
        await Assert.That(state.Completed).IsFalse();

        var nullState = new IndunRoundState(null);
        await Assert.That(nullState.TotalRounds).IsEqualTo(0);
        await Assert.That(nullState.Current).IsNull();
    }

    [Test]
    public async Task State_FirstNextRoundStartsAtRoundOne()
    {
        var state = new IndunRoundState(ChallengeTower());
        await Assert.That(state.ApplyNextRound(1)).IsFalse();
        await Assert.That(state.CurrentRound).IsEqualTo(1);
        await Assert.That(state.Current.Round).IsEqualTo(1);
        await Assert.That(state.CurrentSpawnerId).IsEqualTo(188247u);
        await Assert.That(state.Completed).IsFalse();
    }

    [Test]
    public async Task State_SkipJumpsFromRoundOneToFive()
    {
        var state = new IndunRoundState(ChallengeTower());
        state.ApplyNextRound(1);
        await Assert.That(state.ApplyNextRound(4)).IsFalse();
        await Assert.That(state.CurrentRound).IsEqualTo(5);
        await Assert.That(state.Current.BossRound).IsTrue();
    }

    [Test]
    public async Task State_CompletesOnceOnTheLastRound()
    {
        var state = new IndunRoundState(ChallengeTower(3));
        state.ApplyNextRound(1);
        state.ApplyNextRound(1);
        state.ApplyNextRound(1);
        await Assert.That(state.CurrentRound).IsEqualTo(3);
        await Assert.That(state.Completed).IsFalse();

        // The clear of round 3 fires NextRound(+1) once per raise; only the first completes.
        await Assert.That(state.ApplyNextRound(1)).IsTrue();
        await Assert.That(state.Completed).IsTrue();
        await Assert.That(state.ApplyNextRound(1)).IsFalse();
        await Assert.That(state.ApplyNextRound(0)).IsFalse();
        await Assert.That(state.Completed).IsTrue();
        await Assert.That(state.CurrentRound).IsEqualTo(3);
    }

    [Test]
    public async Task State_ExplicitZeroAddCompletesOnce()
    {
        var state = new IndunRoundState(ChallengeTower(21));
        state.ApplyNextRound(1);
        await Assert.That(state.ApplyNextRound(0)).IsTrue();
        await Assert.That(state.ApplyNextRound(0)).IsFalse();
        await Assert.That(state.CurrentRound).IsEqualTo(1);
    }

    [Test]
    public async Task State_StartRoundSetsPlayingAndArmsTheTimer()
    {
        var state = new IndunRoundState(ChallengeTower());
        state.ApplyNextRound(1);
        state.StartRound(T0);
        await Assert.That(state.Playing).IsTrue();
        await Assert.That(state.IsTimerRunning(T0.AddSeconds(60))).IsTrue();
        await Assert.That(state.IsTimerRunning(T0.AddSeconds(121))).IsFalse();
    }

    [Test]
    public async Task State_RoundsWithoutATimerNeverRunOne()
    {
        var state = new IndunRoundState(ChallengeTower());
        state.ApplyNextRound(1);
        state.ApplyNextRound(1);
        state.StartRound(T0);
        await Assert.That(state.Current.TimerSeconds).IsEqualTo(0);
        await Assert.That(state.IsTimerRunning(T0.AddSeconds(1))).IsFalse();
    }

    [Test]
    public async Task State_TimerIsOffBeforeStartAndAfterEnd()
    {
        var state = new IndunRoundState(ChallengeTower());
        state.ApplyNextRound(1);
        await Assert.That(state.IsTimerRunning(T0)).IsFalse();
        state.StartRound(T0);
        state.EndRound();
        await Assert.That(state.Playing).IsFalse();
        await Assert.That(state.IsTimerRunning(T0.AddSeconds(1))).IsFalse();
    }

    [Test]
    public async Task State_EndRoundSucceedsOnlyAfterANextRound()
    {
        var state = new IndunRoundState(ChallengeTower());
        state.ApplyNextRound(1);
        state.StartRound(T0);
        // "All dead": end alarm with no NextRound in between.
        await Assert.That(state.EndRound()).IsFalse();

        state.StartRound(T0);
        state.ApplyNextRound(1);
        await Assert.That(state.NextRoundRanSinceStart).IsTrue();
        await Assert.That(state.EndRound()).IsTrue();
        await Assert.That(state.NextRoundRanSinceStart).IsFalse();
    }

    [Test]
    public async Task State_NextRoundIsBossReadsTheFollowingRow()
    {
        var state = new IndunRoundState(ChallengeTower());
        state.ApplyNextRound(1);
        state.ApplyNextRound(3);
        await Assert.That(state.CurrentRound).IsEqualTo(4);
        await Assert.That(state.NextRoundIsBoss).IsTrue();
    }

    [Test]
    public async Task State_MailRewardIsClaimedOncePerKind()
    {
        var state = new IndunRoundState(ChallengeTower());
        await Assert.That(state.TryMarkMailReward(6)).IsTrue();
        await Assert.That(state.TryMarkMailReward(6)).IsFalse();
        await Assert.That(state.TryMarkMailReward(7)).IsTrue();
    }
}
