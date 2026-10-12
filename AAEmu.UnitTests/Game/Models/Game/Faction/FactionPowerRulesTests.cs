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
