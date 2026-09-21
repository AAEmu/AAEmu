using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Heroes;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Heroes;

public class HeroElectionRulesTests
{
    [Test]
    public async Task HoldsSeat_OnlyRanksInsideTheRewardCount()
    {
        await Assert.That(HeroElectionRules.HoldsSeat(1, 6)).IsTrue();
        await Assert.That(HeroElectionRules.HoldsSeat(6, 6)).IsTrue();
        await Assert.That(HeroElectionRules.HoldsSeat(7, 6)).IsFalse();
        await Assert.That(HeroElectionRules.HoldsSeat(0, 6)).IsFalse();
    }

    [Test]
    public async Task CanIssueMobilizationOrder_ZeroCapClosesTheFeature()
    {
        await Assert.That(HeroElectionRules.CanIssueMobilizationOrder(0, 0)).IsFalse();
        await Assert.That(HeroElectionRules.CanIssueMobilizationOrder(0, 5)).IsTrue();
        await Assert.That(HeroElectionRules.CanIssueMobilizationOrder(5, 5)).IsFalse();
    }

    [Test]
    public async Task RollLeadershipPeriod_SnapshotsCurrentIncludingZero()
    {
        await Assert.That(HeroElectionRules.RollLeadershipPeriod(0, 1200)).IsEqualTo((1200, 0));
        await Assert.That(HeroElectionRules.RollLeadershipPeriod(1200, 0)).IsEqualTo((0, 0));
        await Assert.That(HeroElectionRules.RollLeadershipPeriod(400, 900)).IsEqualTo((900, 0));
    }

    [Test]
    public async Task MobilizationTransfer_RejectsAMissingFlagAndLoadsANewInstance()
    {
        await Assert.That(HeroElectionRules.CanTransferToMobilizationFlag(false, false)).IsFalse();
        await Assert.That(HeroElectionRules.CanTransferToMobilizationFlag(true, false)).IsFalse();
        await Assert.That(HeroElectionRules.CanTransferToMobilizationFlag(true, true)).IsTrue();
        await Assert.That(HeroElectionRules.NeedsInstanceLoad(1, 1)).IsFalse();
        await Assert.That(HeroElectionRules.NeedsInstanceLoad(1, 2)).IsTrue();
    }

    [Test]
    public async Task CanAcceptMobilizationOrder_WindowAndThresholds()
    {
        var now = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var open = now.AddSeconds(60);
        await Assert.That(HeroElectionRules.CanAcceptMobilizationOrder(now, open, 30, 0, 30, 0)).IsTrue();
        await Assert.That(HeroElectionRules.CanAcceptMobilizationOrder(now, now, 30, 0, 30, 0)).IsFalse();
        await Assert.That(HeroElectionRules.CanAcceptMobilizationOrder(now, open, 29, 0, 30, 0)).IsFalse();
        await Assert.That(HeroElectionRules.CanAcceptMobilizationOrder(now, open, 30, 4, 30, 5)).IsFalse();
    }

    [Test]
    public async Task CanAcceptMobilizationAgainThisHour_OneAcceptPerUtcHour()
    {
        var noon = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        await Assert.That(HeroElectionRules.CanAcceptMobilizationAgainThisHour(DateTime.UnixEpoch, noon)).IsTrue();
        await Assert.That(HeroElectionRules.CanAcceptMobilizationAgainThisHour(noon, noon.AddMinutes(59))).IsFalse();
        await Assert.That(HeroElectionRules.CanAcceptMobilizationAgainThisHour(noon, noon.AddHours(1))).IsTrue();
        await Assert.That(HeroElectionRules.IsSameUtcHour(noon, noon.AddMinutes(59))).IsTrue();
        await Assert.That(HeroElectionRules.IsSameUtcHour(noon, noon.AddHours(1))).IsFalse();
    }

    [Test]
    public async Task IsMobilizationOrderMutedToday_UtcDayAndEpoch()
    {
        var noon = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(noon, DateTimeKind.Unspecified);
        await Assert.That(HeroElectionRules.IsMobilizationOrderMutedToday(DateTime.UnixEpoch, noon)).IsFalse();
        await Assert.That(HeroElectionRules.IsMobilizationOrderMutedToday(default, noon)).IsFalse();
        await Assert.That(HeroElectionRules.IsMobilizationOrderMutedToday(noon, noon.AddHours(11))).IsTrue();
        await Assert.That(HeroElectionRules.IsMobilizationOrderMutedToday(unspecified, noon.AddHours(11))).IsTrue();
        await Assert.That(HeroElectionRules.IsMobilizationOrderMutedToday(noon, noon.Date.AddDays(1))).IsFalse();
        await Assert.That(HeroElectionRules.MobilizationOrderNotRecvStamp(true, noon)).IsEqualTo(noon);
        await Assert.That(HeroElectionRules.MobilizationOrderNotRecvStamp(false, noon)).IsEqualTo(DateTime.UnixEpoch);
    }

    [Test]
    public async Task ShouldOfferMobilizationOrder_MuteAndHourGate()
    {
        var noon = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var epoch = DateTime.UnixEpoch;
        await Assert.That(HeroElectionRules.ShouldOfferMobilizationOrder(epoch, epoch, noon)).IsTrue();
        await Assert.That(HeroElectionRules.ShouldOfferMobilizationOrder(epoch, noon, noon.AddMinutes(30))).IsFalse();
        await Assert.That(HeroElectionRules.ShouldOfferMobilizationOrder(epoch, noon, noon.AddHours(1))).IsTrue();
        await Assert.That(HeroElectionRules.ShouldOfferMobilizationOrder(noon, epoch, noon.AddHours(2))).IsFalse();
        await Assert.That(HeroElectionRules.ShouldOfferMobilizationOrder(noon.Date.AddDays(-1), epoch, noon)).IsTrue();
        await Assert.That(HeroElectionRules.ShouldOfferMobilizationOrder(noon, noon, noon.AddMinutes(10))).IsFalse();
    }

    [Test]
    public async Task TryPickRallyStand_PicksTheNearestPad()
    {
        var pads = new[]
        {
            new HeroElectionRules.RallyStand(100, 0, 10, 0, 183),
            new HeroElectionRules.RallyStand(2, 1, 10, 1.5f, 183),
            new HeroElectionRules.RallyStand(50, 50, 10, 0, 133)
        };

        await Assert.That(HeroElectionRules.TryPickRallyStand(0, 0, pads, out var stand)).IsTrue();
        await Assert.That(stand.X).IsEqualTo(2f);
        await Assert.That(stand.Y).IsEqualTo(1f);
        await Assert.That(stand.YawRad).IsEqualTo(1.5f);
        await Assert.That(HeroElectionRules.TryPickRallyStand(0, 0, [], out _)).IsFalse();
    }

    [Test]
    public async Task PadsForFlagZone_KeepsOnlyTheFlagZone()
    {
        var pads = new[]
        {
            new HeroElectionRules.RallyStand(100, 0, 10, 0, 183),
            new HeroElectionRules.RallyStand(2, 1, 10, 1.5f, 133),
            new HeroElectionRules.RallyStand(50, 50, 10, 0, 133)
        };

        var in183 = HeroElectionRules.PadsForFlagZone(183, pads);
        await Assert.That(in183.Count).IsEqualTo(1);
        await Assert.That(in183[0].ZoneId).IsEqualTo(183u);

        var in133 = HeroElectionRules.PadsForFlagZone(133, pads);
        await Assert.That(in133.Count).IsEqualTo(2);
        await Assert.That(HeroElectionRules.TryPickRallyStand(0, 0, in133, out var stand)).IsTrue();
        await Assert.That(stand.X).IsEqualTo(2f);

        await Assert.That(HeroElectionRules.PadsForFlagZone(999, pads).Count).IsEqualTo(0);
        await Assert.That(HeroElectionRules.PadsForFlagZone(0, pads).Count).IsEqualTo(0);
        await Assert.That(HeroElectionRules.PadsForFlagZone(183, []).Count).IsEqualTo(0);
    }

    [Test]
    public async Task DominionPointState_DailyCapUsesNextUtcMidnight()
    {
        var now = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var gives = new[] { now.AddHours(-1), now.AddHours(-2), now.AddHours(-3) };

        var state = HeroElectionRules.DominionPointState(gives, now, dailyMax: 3, cooldown: TimeSpan.FromMinutes(30));

        await Assert.That(state.Daily).IsEqualTo(3u);
        await Assert.That(state.Weekly).IsEqualTo(3u);
        await Assert.That(state.RemainSeconds).IsEqualTo(HeroElectionRules.SecondsUntilNextUtcDay(now));
    }

    [Test]
    public async Task DominionPointState_CooldownFromNewestGive()
    {
        var now = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var gives = new[] { now.AddMinutes(-10) };

        var state = HeroElectionRules.DominionPointState(gives, now, dailyMax: 3, cooldown: TimeSpan.FromMinutes(30));

        await Assert.That(state.Daily).IsEqualTo(1u);
        await Assert.That(state.RemainSeconds).IsEqualTo(20u * 60);
    }

    [Test]
    public async Task MergeAndRankCandidates_LiveLeadershipBeatsUnsavedPersistedZero()
    {
        var persisted = Array.Empty<(uint, int)>();
        var live = new[] { (CharacterId: 8u, Level: 55, Points: 5000) };

        var ranked = HeroElectionRules.MergeAndRankCandidates(persisted, live, 55, 500, 16);

        await Assert.That(ranked.Count).IsEqualTo(1);
        await Assert.That(ranked[0].CharacterId).IsEqualTo(8u);
        await Assert.That(ranked[0].Points).IsEqualTo(5000);
        await Assert.That(HeroElectionRules.MeetsCandidateThreshold(55, 499, 55, 500)).IsFalse();
    }

    [Test]
    public async Task CanCastBallot_CapsPicksAtSeatsAndChecksVoterGates()
    {
        await Assert.That(HeroElectionRules.CanCastBallot(3, 6, 50, 100, 50, 100)).IsTrue();
        await Assert.That(HeroElectionRules.CanCastBallot(7, 6, 50, 100, 50, 100)).IsFalse();
        await Assert.That(HeroElectionRules.CanCastBallot(1, 6, 49, 100, 50, 100)).IsFalse();
        await Assert.That(HeroElectionRules.CanCastBallot(1, 6, 50, 99, 50, 100)).IsFalse();
    }

    [Test]
    public async Task PendingMailCharacterIds_SkipsAlreadySentRows()
    {
        var pending = HeroElectionRules.PendingMailCharacterIds(
        [
            (1u, true),
            (2u, false),
            (3u, false)
        ]);

        await Assert.That(pending).IsEquivalentTo(new[] { 2u, 3u });
    }

    [Test]
    public async Task BallotPickCount_ClipsToRemainingWireNotTheClientCount()
    {
        const int twoPicksAndVoter = sizeof(ulong) * 3;
        await Assert.That(HeroElectionRules.BallotPickCount(1_000_000, twoPicksAndVoter)).IsEqualTo(2);
        await Assert.That(HeroElectionRules.BallotPickCount(1, twoPicksAndVoter)).IsEqualTo(1);
        await Assert.That(HeroElectionRules.BallotPickCount(-3, twoPicksAndVoter)).IsEqualTo(0);
        await Assert.That(HeroElectionRules.BallotPickCount(2, sizeof(ulong))).IsEqualTo(0);
    }

    [Test]
    public async Task PairBonusesWithGrades_HighestGradeGetsRichestTier()
    {
        var rewards = new[]
        {
            new HeroReward { HeroGradeId = 3, Ranking = 1 },
            new HeroReward { HeroGradeId = 2, Ranking = 2 },
            new HeroReward { HeroGradeId = 1, Ranking = 3 }
        };
        var bonuses = new[]
        {
            new HeroBonus { Id = 10, LeadershipPoint = 100 },
            new HeroBonus { Id = 11, LeadershipPoint = 50 }
        };

        var map = HeroGameData.PairBonusesWithGrades(rewards, bonuses);

        await Assert.That(map[3].Id).IsEqualTo(10u);
        await Assert.That(map[2].Id).IsEqualTo(11u);
        await Assert.That(map.ContainsKey(1)).IsFalse();
    }

    [Test]
    public async Task MeetsBonusConditions_RequiresEveryAssignmentCount()
    {
        var bonus = new HeroBonus { LeadershipPoint = 10, MobilizationOrderCount = 1 };
        var assignments = new[]
        {
            new HeroBonusTodayAssignment { TodayQuestStepId = 1, Count = 2 },
            new HeroBonusTodayAssignment { TodayQuestStepId = 2, Count = 1 }
        };
        var progress = new Dictionary<uint, int> { [1] = 2, [2] = 1 };

        await Assert.That(HeroElectionRules.MeetsBonusConditions(10, 1, bonus, assignments, progress)).IsTrue();
        await Assert.That(HeroElectionRules.MeetsBonusConditions(9, 1, bonus, assignments, progress)).IsFalse();
        await Assert.That(HeroElectionRules.MeetsBonusConditions(10, 1, bonus, assignments, new Dictionary<uint, int> { [1] = 2 })).IsFalse();
    }

    [Test]
    public async Task BuildEventStateEntries_MarksLeftPhaseOnce()
    {
        var running = new Dictionary<uint, (HeroPhase Phase, uint SeasonId)>
        {
            [148] = (HeroPhase.HeroVoting, 2)
        };

        var entries = HeroElectionRules.BuildEventStateEntries(running, (1, HeroPhase.HeroAbstain));

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.Exists(e => e.ScheduleEvent == HeroPhase.HeroVoting && e.State == HeroElectionRules.StateEntering)).IsTrue();
        await Assert.That(entries.Exists(e => e.ScheduleEvent == HeroPhase.HeroAbstain && e.State == HeroElectionRules.StateLeaving)).IsTrue();
    }
}
