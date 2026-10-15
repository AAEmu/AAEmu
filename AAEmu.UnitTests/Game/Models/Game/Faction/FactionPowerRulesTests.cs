using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.UnitTests.Game.Models.Game.Faction;

public class FactionPowerRulesTests
{
    private const uint Nuia = 148, Haranya = 149, Outlaw = 114;

    [Test]
    public async Task WeekNumber_StartsOnSunday()
    {
        var saturday = new DateTime(2026, 10, 10, 23, 59, 0, DateTimeKind.Utc);
        var sunday = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(FactionPowerRules.WeekNumber(sunday)).IsEqualTo(FactionPowerRules.WeekNumber(saturday) + 1);
        await Assert.That(FactionPowerRules.WeekStart(FactionPowerRules.WeekNumber(sunday))).IsEqualTo(sunday);
    }

    [Test]
    public async Task SiegeAwardWeek_IsTheWeekTheSiegeEnded()
    {
        var saturdayEnd = new DateTime(2026, 10, 10, 22, 0, 0, DateTimeKind.Utc);
        var saturdayWeek = FactionPowerRules.WeekNumber(saturdayEnd);
        var settledMonday = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
        var midnightEnd = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

        // Server down over the rollover: the win still belongs to the week it was fought in.
        await Assert.That(FactionPowerRules.SiegeAwardWeek(saturdayEnd, settledMonday)).IsEqualTo(saturdayWeek);
        // A siege ending exactly at the rollover belongs to the week before it.
        await Assert.That(FactionPowerRules.SiegeAwardWeek(midnightEnd, settledMonday)).IsEqualTo(saturdayWeek);
        // Unknown or future end: the current week.
        await Assert.That(FactionPowerRules.SiegeAwardWeek(null, settledMonday)).IsEqualTo(saturdayWeek + 1);
        await Assert.That(FactionPowerRules.SiegeAwardWeek(settledMonday.AddDays(1), settledMonday)).IsEqualTo(saturdayWeek + 1);
    }

    [Test]
    public async Task NewWeekRow_CarriesTheMaintainShareOfLastWeek()
    {
        var last = new FactionPowerScore { WeekNum = 9, FactionId = Nuia, SiegePoint = 600, SiegeMaintainPoint = 200, RaidPoint = 100, RaidMaintainPoint = 0, QuestPoint = 50 };

        var row = FactionPowerRules.NewWeekRow(10, Nuia, last, 50, 70);
        var empty = FactionPowerRules.NewWeekRow(10, Haranya, null, 50, 70);

        await Assert.That(row.WeekNum).IsEqualTo(10u);
        await Assert.That(row.SiegeMaintainPoint).IsEqualTo(400u);
        await Assert.That(row.RaidMaintainPoint).IsEqualTo(70u);
        await Assert.That(row.SiegePoint).IsEqualTo(0u);
        await Assert.That(row.QuestPoint).IsEqualTo(0f);
        await Assert.That(FactionPowerRules.Total(empty)).IsEqualTo(0);
    }

    [Test]
    public async Task PlanSiegeAward_ThisWeek_WritesTheWholeRowWithItsCarriedPoints()
    {
        var thisWeek = new FactionPowerScore { WeekNum = 10, FactionId = Nuia, SiegePoint = 100, SiegeMaintainPoint = 300, RaidMaintainPoint = 70 };

        var (defer, won, carried) = FactionPowerRules.PlanSiegeAward(10, 600, 10, null, thisWeek, 50);

        await Assert.That(defer).IsFalse();
        await Assert.That(carried).IsNull();
        await Assert.That(won.SiegePoint).IsEqualTo(700u);
        await Assert.That(won.SiegeMaintainPoint).IsEqualTo(300u);
        await Assert.That(won.RaidMaintainPoint).IsEqualTo(70u);
        // The live row is only changed once the award is saved.
        await Assert.That(thisWeek.SiegePoint).IsEqualTo(100u);
    }

    [Test]
    public async Task PlanSiegeAward_RetriedAfterTheRollover_CreditsLastWeekAndItsCarryOver()
    {
        var lastWeek = new FactionPowerScore { WeekNum = 9, FactionId = Nuia, SiegePoint = 200, SiegeMaintainPoint = 100, QuestPoint = 5 };
        var thisWeek = FactionPowerRules.NewWeekRow(10, Nuia, lastWeek, 50, 70);
        thisWeek.QuestPoint = 3;

        var (defer, won, carried) = FactionPowerRules.PlanSiegeAward(9, 600, 10, lastWeek, thisWeek, 50);

        await Assert.That(defer).IsFalse();
        await Assert.That(won.WeekNum).IsEqualTo(9u);
        await Assert.That(won.SiegePoint).IsEqualTo(800u);
        await Assert.That(won.QuestPoint).IsEqualTo(5f);
        await Assert.That(thisWeek.SiegeMaintainPoint).IsEqualTo(150u);
        await Assert.That(carried.WeekNum).IsEqualTo(10u);
        await Assert.That(carried.SiegeMaintainPoint).IsEqualTo(450u);
        await Assert.That(carried.QuestPoint).IsEqualTo(3f);
    }

    [Test]
    public async Task PlanSiegeAward_LastWeekWithoutARow_StartsOneFromZero()
    {
        var thisWeek = new FactionPowerScore { WeekNum = 10, FactionId = Haranya };

        var (_, won, carried) = FactionPowerRules.PlanSiegeAward(9, 600, 10, null, thisWeek, 50);

        await Assert.That(won.FactionId).IsEqualTo(Haranya);
        await Assert.That(won.WeekNum).IsEqualTo(9u);
        await Assert.That(won.SiegePoint).IsEqualTo(600u);
        await Assert.That(carried.SiegeMaintainPoint).IsEqualTo(300u);
    }

    [Test]
    public async Task PlanSiegeAward_DefersAWeekNotRolledOverToAndLeavesOlderWeeksToTheDatabase()
    {
        var thisWeek = new FactionPowerScore { WeekNum = 10, FactionId = Nuia };

        var ahead = FactionPowerRules.PlanSiegeAward(11, 600, 10, null, thisWeek, 50);
        var older = FactionPowerRules.PlanSiegeAward(7, 600, 10, null, thisWeek, 50);

        await Assert.That(ahead.Defer).IsTrue();
        await Assert.That(ahead.Won).IsNull();
        await Assert.That(older.Defer).IsFalse();
        await Assert.That(older.Won).IsNull();
        await Assert.That(older.Carried).IsNull();
    }

    [Test]
    public async Task Total_AddsTheCountersLikeTheClient()
    {
        var score = new FactionPowerScore
        {
            SiegePoint = 600, SiegeMaintainPoint = 300, CompetitionPoint = 50, QuestPoint = 2.5f,
            RaidPoint = 100, RaidMaintainPoint = 70, RaidJointPoint = 20, ImmigratePoint = 40
        };

        await Assert.That(FactionPowerRules.Total(score)).IsEqualTo(1183);
    }

    [Test]
    public async Task Classify_TopIsSuperiorBottomIsInferior()
    {
        var (superiors, inferiors) = FactionPowerRules.Classify(
            new Dictionary<uint, int> { [Nuia] = 3000, [Haranya] = 2000, [Outlaw] = 1200 }, 1000, 110);

        await Assert.That(superiors.SetEquals([Nuia])).IsTrue();
        await Assert.That(inferiors.SetEquals([Outlaw])).IsTrue();
    }

    [Test]
    public async Task Classify_CloseRunnerUpSharesTheStanding()
    {
        var (superiors, inferiors) = FactionPowerRules.Classify(
            new Dictionary<uint, int> { [Nuia] = 3000, [Haranya] = 2800, [Outlaw] = 1200 }, 1000, 110);
        await Assert.That(superiors.SetEquals([Nuia, Haranya])).IsTrue();
        await Assert.That(inferiors.SetEquals([Outlaw])).IsTrue();

        (superiors, inferiors) = FactionPowerRules.Classify(
            new Dictionary<uint, int> { [Nuia] = 3000, [Haranya] = 1300, [Outlaw] = 1200 }, 1000, 110);
        await Assert.That(superiors.SetEquals([Nuia])).IsTrue();
        await Assert.That(inferiors.SetEquals([Haranya, Outlaw])).IsTrue();
    }

    [Test]
    public async Task Classify_QuietServerHasNoStandings()
    {
        var (superiors, inferiors) = FactionPowerRules.Classify(
            new Dictionary<uint, int> { [Nuia] = 900, [Haranya] = 100, [Outlaw] = 0 }, 1000, 110);

        await Assert.That(superiors.Count + inferiors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Overrides_ReplaceTheComputedStanding()
    {
        var superiors = new HashSet<uint> { Nuia };
        var inferiors = new HashSet<uint> { Outlaw };

        FactionPowerRules.ApplyOverrides(superiors, inferiors, new Dictionary<uint, FactionPowerStanding>
        {
            [Nuia] = FactionPowerStanding.Normal,
            [Haranya] = FactionPowerStanding.Inferior
        });

        await Assert.That(superiors.Count).IsEqualTo(0);
        await Assert.That(inferiors.SetEquals([Haranya, Outlaw])).IsTrue();
    }

    [Test]
    public async Task Maintain_KeepsTheConfiguredShare()
    {
        await Assert.That(FactionPowerRules.Maintain(600, 300, 50)).IsEqualTo(450u);
    }

    [Test]
    public async Task Weights_PercentGroupsScaleAndOthersMultiply()
    {
        var siege = new List<(int, int)> { (1, 50), (10, 100), (30, 120) };
        var raid2 = new List<(int, int)> { (1, 1), (20, 2), (50, 3) };

        await Assert.That(FactionPowerWeightRules.Apply(600, 1, siege, 15, true)).IsEqualTo(600d);
        await Assert.That(FactionPowerWeightRules.Apply(600, 1, siege, 1, true)).IsEqualTo(300d);
        await Assert.That(FactionPowerWeightRules.Apply(20, 4, raid2, 25, false)).IsEqualTo(40d);
        await Assert.That(FactionPowerWeightRules.Apply(20, 0, null, 25, false)).IsEqualTo(20d);
    }

    [Test]
    public async Task Packet_WritesSetsWeekScoresFlagAndConfigs()
    {
        var score = new FactionPowerScore { WeekNum = 7, FactionId = Nuia, QuestPoint = 1.5f };
        var body = new SCFactionPowerScorePacket([Nuia], [Haranya], 7, [score], [Haranya, Nuia]).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(1u); expected.Write(Nuia);
        expected.Write(1u); expected.Write(Haranya);
        expected.Write(7u);
        expected.Write(1u);
        expected.Write(7u); expected.Write((int)Nuia); expected.Write(0u); expected.Write(0u); expected.Write(0u);
        expected.Write(1.5f); expected.Write(0u); expected.Write(0u); expected.Write(0u); expected.Write(0u);
        expected.Write(true);
        expected.Write(2u); expected.Write(Nuia); expected.Write(Haranya);

        await Assert.That(Convert.ToHexString(body)).IsEqualTo(Convert.ToHexString(expected.GetBytes()));
    }
}
