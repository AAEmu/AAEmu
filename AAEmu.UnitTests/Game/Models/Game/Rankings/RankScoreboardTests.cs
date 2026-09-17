using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Models.Game.Rankings;

public class RankScoreboardTests
{
    private static RankScore Score(ulong holder, long value)
    {
        return new RankScore
        {
            RankId = 23,
            HolderKind = RankHolderKind.Character,
            HolderId = holder,
            Value = value,
            BareValue = 0
        };
    }

    [Test]
    public async Task Place_OrdersByValueBestFirst()
    {
        var places = RankScoreboard.Place([Score(1, 500), Score(2, 900), Score(3, 700)], permitTie: false);

        await Assert.That(places.Select(place => place.Score.HolderId).ToArray()).IsEquivalentTo(new ulong[] { 2, 3, 1 });
        await Assert.That(places.Select(place => place.Position).ToArray()).IsEquivalentTo(new uint[] { 1, 2, 3 });
    }

    [Test]
    public async Task Place_WhenTiesArePermitted_EqualValuesShareThePlace()
    {
        var places = RankScoreboard.Place([Score(1, 900), Score(2, 900), Score(3, 700)], permitTie: true);

        await Assert.That(places[0].Position).IsEqualTo(1u);
        await Assert.That(places[1].Position).IsEqualTo(1u);
        await Assert.That(places[2].Position).IsEqualTo(3u);
    }

    [Test]
    public async Task Place_WhenTiesAreNotPermitted_EqualValuesTakeTheNextPlaces()
    {
        var places = RankScoreboard.Place([Score(1, 900), Score(2, 900), Score(3, 700)], permitTie: false);

        await Assert.That(places[0].Position).IsEqualTo(1u);
        await Assert.That(places[1].Position).IsEqualTo(2u);
        await Assert.That(places[2].Position).IsEqualTo(3u);
    }

    [Test]
    public async Task Place_EqualValuesAreOrderedByHolder_SoABoardReadsTheSameTwice()
    {
        var first = RankScoreboard.Place([Score(9, 900), Score(4, 900)], permitTie: true);
        var second = RankScoreboard.Place([Score(4, 900), Score(9, 900)], permitTie: true);

        await Assert.That(first.Select(place => place.Score.HolderId).ToArray())
            .IsEquivalentTo(second.Select(place => place.Score.HolderId).ToArray());
        await Assert.That(first[0].Score.HolderId).IsEqualTo(4UL);
    }

    [Test]
    public async Task Place_WithNoHolders_IsEmpty()
    {
        await Assert.That(RankScoreboard.Place([], permitTie: true)).IsEmpty();
        await Assert.That(RankScoreboard.Place(null, permitTie: true)).IsEmpty();
    }
}
