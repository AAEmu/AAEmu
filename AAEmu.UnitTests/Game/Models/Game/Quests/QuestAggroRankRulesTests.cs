using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestAggroRankRulesTests
{
    // ---------------------------------------------------------------- percentile

    // The contract the objective's cut-offs are written against: the strongest aggro on
    // the table is percentile 0. Counting must not change that, whichever way the
    // entries happen to be enumerated.
    [Test]
    [Arguments(1000)]
    [Arguments(1)]
    public async Task TopOfTheTable_IsPercentileZero(int topAggro)
    {
        int[] table = [topAggro, topAggro - 100, topAggro - 200];
        await Assert.That(QuestAggroRankRules.PercentileFor(topAggro, table)).IsEqualTo(0f);
    }

    // A single entry on the table is both the top and the bottom, and still scores 0.
    [Test]
    public async Task SoloAggroTable_IsPercentileZero()
    {
        await Assert.That(QuestAggroRankRules.PercentileFor(500, [500])).IsEqualTo(0f);
    }

    // A unit that is not on the table has no aggro at all, which is the worst percentile.
    [Test]
    public async Task EmptyAggroTable_IsNoAggro()
    {
        await Assert.That(QuestAggroRankRules.PercentileFor(0, [])).IsEqualTo(100f);
        await Assert.That(QuestAggroRankRules.NoAggroPercentile).IsEqualTo(100f);
    }

    // The bottom of the table is out-ranked by every other entry but never reaches 100 -
    // 100 is reserved for "not on the table", which is what a row's rank2 = 0 needs to
    // mean "the very top only".
    [Test]
    public async Task BottomOfTheTable_IsJustUnderNoAggro()
    {
        int[] table = [900, 600, 300, 100];
        await Assert.That(QuestAggroRankRules.PercentileFor(100, table)).IsEqualTo(75f);
    }

    // The percentile is the share of the table that out-aggroed the unit, in percent:
    // 1 of 5 out-ranks the second-placed unit, 4 of 5 out-rank the last.
    [Test]
    public async Task Percentile_IsTheShareOfTheTableThatOutAggroed()
    {
        int[] table = [1000, 800, 600, 400, 200];
        await Assert.That(QuestAggroRankRules.PercentileFor(1000, table)).IsEqualTo(0f);
        await Assert.That(QuestAggroRankRules.PercentileFor(800, table)).IsEqualTo(20f);
        await Assert.That(QuestAggroRankRules.PercentileFor(600, table)).IsEqualTo(40f);
        await Assert.That(QuestAggroRankRules.PercentileFor(400, table)).IsEqualTo(60f);
        await Assert.That(QuestAggroRankRules.PercentileFor(200, table)).IsEqualTo(80f);
    }

    // The order of the argument list must not matter: two units that tied score the same
    // percentile, and a stable sort over a concurrent table cannot promise that.
    [Test]
    public async Task TiedAggro_SharesOnePercentile()
    {
        int[] tied = [900, 600, 300, 300];
        int[] reversed = [300, 300, 600, 900];
        await Assert.That(QuestAggroRankRules.PercentileFor(300, tied))
            .IsEqualTo(QuestAggroRankRules.PercentileFor(300, reversed));
        // Two units hold 300, the other two out-ran them, so both sit at 50.
        await Assert.That(QuestAggroRankRules.PercentileFor(300, tied)).IsEqualTo(50f);
    }

    // The top of a table where everything tied is still percentile 0 for all of them.
    [Test]
    public async Task AllTied_IsPercentileZeroForEveryone()
    {
        int[] table = [500, 500, 500, 500];
        await Assert.That(QuestAggroRankRules.PercentileFor(500, table)).IsEqualTo(0f);
    }

    // Every percentile the rules can produce stays inside 0-100, whatever the table.
    [Test]
    public async Task Percentile_StaysInsideZeroToOneHundred()
    {
        int[] table = [10, 9, 8, 7, 6, 5, 4, 3, 2, 1];
        for (var own = 0; own <= 12; own++)
        {
            var pct = QuestAggroRankRules.PercentileFor(own, table);
            await Assert.That(pct >= 0f && pct <= 100f).IsTrue();
        }
    }

    // ---------------------------------------------------------------- bands

    // quest_act_obj_aggros 4 and 152: 10 / 20 / 30 cut-offs paying 100 / 50 / 30.
    [Test]
    public async Task Band_TenTwentyThirty_ThreeBandsPlusNothing()
    {
        await Assert.That(QuestAggroRankRules.Band(0f, 10, 20, 30)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(10f, 10, 20, 30)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(20f, 10, 20, 30)).IsEqualTo(2);
        await Assert.That(QuestAggroRankRules.Band(30f, 10, 20, 30)).IsEqualTo(3);
        await Assert.That(QuestAggroRankRules.Band(30.1f, 10, 20, 30)).IsEqualTo(0);
        await Assert.That(QuestAggroRankRules.Band(100f, 10, 20, 30)).IsEqualTo(0);
    }

    // quest_act_obj_aggros 10, 11, 23: 55 / 70 / 90 cut-offs paying 100 / 50 / 10.
    [Test]
    public async Task Band_FiftyFiveSeventyNinety_CoversMostOfTheTable()
    {
        await Assert.That(QuestAggroRankRules.Band(0f, 55, 70, 90)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(55f, 55, 70, 90)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(70f, 55, 70, 90)).IsEqualTo(2);
        await Assert.That(QuestAggroRankRules.Band(90f, 55, 70, 90)).IsEqualTo(3);
        await Assert.That(QuestAggroRankRules.Band(90.1f, 55, 70, 90)).IsEqualTo(0);
    }

    // 86 of the 107 rows are rank1 = 100 with rank2 = rank3 = 0: everyone is paid, and the
    // degenerate 0 cut-offs never steal the band because the widest one is checked first.
    [Test]
    public async Task Band_HundredWithZeroLowerBands_PaysEveryoneTheTopBand()
    {
        await Assert.That(QuestAggroRankRules.Band(0f, 100, 0, 0)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(99.9f, 100, 0, 0)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(100f, 100, 0, 0)).IsEqualTo(1);
    }

    // Row 43 ships rank1 = 99, rank2 = 1, rank3 NULL (loaded as 0).
    [Test]
    public async Task Band_NullRankLoadedAsZero_KeepsTheWideBandFirst()
    {
        await Assert.That(QuestAggroRankRules.Band(0f, 99, 1, 0)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(1f, 99, 1, 0)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(1.5f, 99, 1, 0)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(99f, 99, 1, 0)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(100f, 99, 1, 0)).IsEqualTo(0);
    }

    // Nothing may be awarded past the lowest cut-off: the objective stays unfulfilled and
    // the quest's reward ratio stays 0.
    [Test]
    public async Task Band_PastEveryCutOff_PaysNothing()
    {
        await Assert.That(QuestAggroRankRules.Band(100f, 10, 20, 30)).IsEqualTo(0);
        await Assert.That(QuestAggroRankRules.Band(95f, 10, 20, 30)).IsEqualTo(0);
    }
}
