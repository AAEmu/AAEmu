using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.UnitTests.Game.Models.Game.Sieges;

public class SiegeScoreStateTests
{
    private const ushort ZoneGroup = 33;

    private static SiegeScoreState Score(uint outlaw = 0, uint defense = 0, uint offense = 0) => new()
    {
        ZoneGroupId = ZoneGroup,
        OutlawPoint = outlaw,
        DefensePoint = defense,
        OffensePoint = offense,
    };

    [Test]
    public async Task For_ReadsTheCounterOfTheSide()
    {
        var state = Score(outlaw: 7, defense: 11, offense: 13);

        await Assert.That(state.For(SiegeScoreSide.Outlaw)).IsEqualTo(7u);
        await Assert.That(state.For(SiegeScoreSide.Defense)).IsEqualTo(11u);
        await Assert.That(state.For(SiegeScoreSide.Offense)).IsEqualTo(13u);
    }

    [Test]
    public async Task Plus_AddsToOneSideAndLeavesTheOthers()
    {
        var next = Score(outlaw: 7, defense: 11, offense: 13).Plus(SiegeScoreSide.Offense, 5);

        await Assert.That(next.OutlawPoint).IsEqualTo(7u);
        await Assert.That(next.DefensePoint).IsEqualTo(11u);
        await Assert.That(next.OffensePoint).IsEqualTo(18u);
    }

    [Test]
    public async Task Plus_LeavesTheStateItWasCalledOnAlone()
    {
        var before = Score(offense: 13);

        _ = before.Plus(SiegeScoreSide.Offense, 5);

        await Assert.That(before.OffensePoint).IsEqualTo(13u);
    }

    [Test]
    public async Task Plus_SaturatesInsteadOfWrappingPastTheCounterMaximum()
    {
        // A counter that wrapped would report a side as having lost ground and could settle a siege the
        // other way round, so the top of the range sticks.
        var next = Score(outlaw: uint.MaxValue - 2).Plus(SiegeScoreSide.Outlaw, 10);

        await Assert.That(next.OutlawPoint).IsEqualTo(uint.MaxValue);
    }

    [Test]
    public async Task Empty_StartsTheZoneGroupAtZero()
    {
        var state = SiegeScoreState.Empty(ZoneGroup);

        await Assert.That(state.ZoneGroupId).IsEqualTo(ZoneGroup);
        await Assert.That(state.OutlawPoint).IsEqualTo(0u);
        await Assert.That(state.DefensePoint).IsEqualTo(0u);
        await Assert.That(state.OffensePoint).IsEqualTo(0u);
    }
}
