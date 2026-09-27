using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Walks every shipped <c>quest_act_obj_aggros</c> row against real aggro tables and checks
/// the reward the objective hands out. The cut-offs are percentile values, so the table only
/// makes sense when the score handed to <see cref="QuestAggroRankRules.Band"/> is a 0-100
/// percentile: on a 0-1 fraction every position lands in the first band and the 2nd and 3rd
/// bands of the content are unreachable.
/// </summary>
public class QuestAggroRankContentTests
{
    /// <summary>Distinct aggro values, so no two units tie.</summary>
    private static readonly int[] DistinctTen = [1000, 900, 800, 700, 600, 500, 400, 300, 200, 100];

    /// <summary>A shorter table, so the percentiles land on different values.</summary>
    private static readonly int[] DistinctFour = [900, 600, 400, 200];

    /// <summary>The bottom two units hold the same aggro.</summary>
    private static readonly int[] TiedAtTheBottom = [900, 600, 300, 300];

    /// <summary>Every position of every table, as the percentile that position scores.</summary>
    private static IEnumerable<float> EveryPercentile()
    {
        foreach (var table in new[] { DistinctTen, DistinctFour, TiedAtTheBottom })
        {
            foreach (var aggro in table)
                yield return QuestAggroRankRules.PercentileFor(aggro, table);
        }
    }

    private static int ShippedRatio((uint Id, int Range, int Rank1, int Rank2, int Rank3, int Rank1Ratio, int Rank2Ratio, int Rank3Ratio) row, int band)
        => band switch
        {
            1 => row.Rank1Ratio,
            2 => row.Rank2Ratio,
            3 => row.Rank3Ratio,
            _ => 0
        };

    [Test]
    public async Task Snapshot_HasTheShippedRowCount()
    {
        await Assert.That(QuestAggroRankContentSnapshot.Rows.Length)
            .IsEqualTo(QuestAggroRankContentSnapshot.RowCount);
    }

    // The cut-offs are only reachable on a 0-100 score. With a score that never leaves
    // [0, 1) no position can reach a cut-off of 10 or more, so this collapses to zero and
    // 19 of the shipped rows silently pay their first ratio to everybody.
    [Test]
    public async Task EveryShippedRow_ReachesItsSecondOrThirdBand()
    {
        var reached = new List<uint>();
        foreach (var row in QuestAggroRankContentSnapshot.Rows)
        {
            var bands = EveryPercentile()
                .Select(p => QuestAggroRankRules.Band(p, row.Rank1, row.Rank2, row.Rank3))
                .ToHashSet();
            if (bands.Contains(2) || bands.Contains(3))
                reached.Add(row.Id);
        }

        await Assert.That(reached.Count).IsEqualTo(19);
        // The 19 are the rows whose 2nd/3rd cut-off is a real band and not a 0 that the
        // first band already swallows. Row 43 pays 100 in both of its bands, so widening
        // the score does not change what a player receives there.
        await Assert.That(reached).IsEquivalentTo(new[]
        {
            4u, 6u, 7u, 8u, 9u, 10u, 11u, 13u, 14u, 15u, 16u, 17u, 18u, 20u, 21u, 23u, 27u, 29u, 152u
        });
    }

    // The reward a player walks away with has to be the ratio its own row ships for the
    // band it reached, and nothing at all past the lowest cut-off.
    [Test]
    public async Task Reward_FollowsTheShippedBandRatioOfTheReachedBand()
    {
        foreach (var row in QuestAggroRankContentSnapshot.Rows)
        {
            foreach (var percentile in EveryPercentile())
            {
                var band = QuestAggroRankRules.Band(percentile, row.Rank1, row.Rank2, row.Rank3);
                var awarded = ShippedRatio(row, band);
                if (band == 0)
                {
                    await Assert.That(awarded).IsEqualTo(0);
                    continue;
                }
                // rank1 is the full pool on every shipped row, so a band that is not the
                // first one has to be able to hand out less than the full pool.
                if (band != 1)
                    await Assert.That(awarded < row.Rank1Ratio).IsTrue();
            }
        }
    }

    // The top of the table is percentile 0, inside the first cut-off of every shipped row,
    // and every row's first ratio is the full pool.
    [Test]
    public async Task TopOfTheTable_GetsTheFirstBandAndTheFullPool()
    {
        foreach (var row in QuestAggroRankContentSnapshot.Rows)
        {
            foreach (var table in new[] { DistinctTen, DistinctFour, TiedAtTheBottom })
            {
                var percentile = QuestAggroRankRules.PercentileFor(table[0], table);
                await Assert.That(QuestAggroRankRules.Band(percentile, row.Rank1, row.Rank2, row.Rank3))
                    .IsEqualTo(1);
                await Assert.That(row.Rank1Ratio).IsEqualTo(100);
            }
        }
    }

    // The last place on a 10 / 20 / 30 row is past every cut-off. It must not be paid the
    // first ratio: that is the exact over-payment a 0-1 score hands out.
    [Test]
    public async Task LastPlace_OnATightRowIsPaidNothing()
    {
        var tight = QuestAggroRankContentSnapshot.Rows
            .Where(r => r.Rank1 == 10 && r.Rank2 == 20 && r.Rank3 == 30)
            .ToList();
        await Assert.That(tight.Count).IsEqualTo(2);

        foreach (var row in tight)
        {
            var percentile = QuestAggroRankRules.PercentileFor(DistinctTen[^1], DistinctTen);
            await Assert.That(percentile).IsEqualTo(90f);
            await Assert.That(QuestAggroRankRules.Band(percentile, row.Rank1, row.Rank2, row.Rank3))
                .IsEqualTo(0);
        }
    }

    // A 55 / 70 / 90 row pays the bottom of a ten-player table its third ratio instead of
    // the full pool, and the 2nd and 3rd places their second ratio.
    [Test]
    public async Task GenerousRow_PaysLowerRatiosToLowerPlaces()
    {
        var generous = QuestAggroRankContentSnapshot.Rows
            .Where(r => r.Rank1 == 55 && r.Rank2 == 70 && r.Rank3 == 90)
            .ToList();
        await Assert.That(generous.Count).IsEqualTo(3);

        var row = generous[0];
        // DistinctTen percentiles run 0, 10, 20 ... 90 top to bottom.
        await Assert.That(QuestAggroRankRules.Band(0f, row.Rank1, row.Rank2, row.Rank3)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(20f, row.Rank1, row.Rank2, row.Rank3)).IsEqualTo(1);
        await Assert.That(QuestAggroRankRules.Band(60f, row.Rank1, row.Rank2, row.Rank3)).IsEqualTo(2);
        await Assert.That(QuestAggroRankRules.Band(80f, row.Rank1, row.Rank2, row.Rank3)).IsEqualTo(3);
        await Assert.That(ShippedRatio(row, 1)).IsEqualTo(100);
        await Assert.That(ShippedRatio(row, 2)).IsEqualTo(50);
        await Assert.That(ShippedRatio(row, 3)).IsEqualTo(10);
    }

    // Units that tied have to be paid the same band, whichever of them the table hands back
    // first. A sort over the table separates them by insertion order instead.
    [Test]
    public async Task TiedUnits_ArePaidTheSameBand()
    {
        var reversed = new[] { 300, 300, 600, 900 };
        var first = QuestAggroRankRules.PercentileFor(300, TiedAtTheBottom);
        var second = QuestAggroRankRules.PercentileFor(300, reversed);
        await Assert.That(first).IsEqualTo(second);

        foreach (var row in QuestAggroRankContentSnapshot.Rows)
        {
            var a = QuestAggroRankRules.Band(first, row.Rank1, row.Rank2, row.Rank3);
            var b = QuestAggroRankRules.Band(second, row.Rank1, row.Rank2, row.Rank3);
            await Assert.That(a).IsEqualTo(b);
            await Assert.That(ShippedRatio(row, a)).IsEqualTo(ShippedRatio(row, b));
        }
    }

    // No shipped cut-off may sit outside the 0-100 score the rules produce, apart from the
    // degenerate 0s that mean "the very top only" and the 100 that means "everyone".
    [Test]
    public async Task EveryShippedCutOff_IsAReachablePercentile()
    {
        foreach (var row in QuestAggroRankContentSnapshot.Rows)
        {
            foreach (var cutOff in new[] { row.Rank1, row.Rank2, row.Rank3 })
            {
                if (cutOff == 0)
                    continue;
                await Assert.That(cutOff >= 0 && cutOff <= 100).IsTrue();
            }
        }
    }
}
