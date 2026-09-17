using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotBranchRulesTests
{
    private static PlotNode Child(int weight, bool fail = false) =>
        new() { ParentNextEvent = new PlotNextEvent { Weight = weight, Fail = fail } };

    [Test]
    public async Task WeightedSiblings_FireExactlyOnePerExecution()
    {
        // Plot 3302 event 16399: three edges, 50/25/25.
        var children = new List<PlotNode> { Child(50), Child(25), Child(25) };
        var random = new Random(20260914);

        for (var i = 0; i < 2_000; i++)
        {
            var picked = PlotBranchRules.SelectEligible(children, random.Next);
            await Assert.That(picked.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task WeightedSiblings_DistributionFollowsTheWeights()
    {
        var children = new List<PlotNode> { Child(50), Child(25), Child(25) };
        var random = new Random(20260914);
        var counts = new int[3];

        const int runs = 20_000;
        for (var i = 0; i < runs; i++)
        {
            var picked = PlotBranchRules.SelectEligible(children, random.Next);
            counts[children.IndexOf(picked[0])]++;
        }

        // 2% of the runs is a wide margin: at 20,000 draws the standard deviation is well under 100 counts.
        await Assert.That(Math.Abs(counts[0] - runs * 0.50) / (double)runs).IsLessThan(0.02);
        await Assert.That(Math.Abs(counts[1] - runs * 0.25) / (double)runs).IsLessThan(0.02);
        await Assert.That(Math.Abs(counts[2] - runs * 0.25) / (double)runs).IsLessThan(0.02);
    }

    [Test]
    public async Task NoWeights_EveryChildRunsInOrderAndNothingIsDrawn()
    {
        // 50,760 plot_next_events rows carry weight 0 (2,111 are weighted). With none of them weighted the
        // eligible set has to come back untouched - and without a draw, so the shared Random stream the
        // Chance and CombatDiceResult conditions read is exactly where it used to be.
        var children = new List<PlotNode> { Child(0), Child(0), Child(0) };
        var draws = 0;

        var picked = PlotBranchRules.SelectEligible(children, total =>
        {
            draws++;
            return 0;
        });

        await Assert.That(picked.Count).IsEqualTo(3);
        await Assert.That(picked[0]).IsSameReferenceAs(children[0]);
        await Assert.That(picked[2]).IsSameReferenceAs(children[2]);
        await Assert.That(draws).IsEqualTo(0);
    }

    [Test]
    public async Task UnweightedSiblings_KeepFiringBesideTheDrawnOne()
    {
        // Event 16252 has nine unweighted edges and two weighted ones.
        var children = new List<PlotNode> { Child(0), Child(30), Child(0), Child(10) };
        var picked = PlotBranchRules.SelectEligible(children, _ => 0);

        await Assert.That(picked.Count).IsEqualTo(3);
        await Assert.That(picked[0].ParentNextEvent.Weight).IsEqualTo(0);
        await Assert.That(picked[1].ParentNextEvent.Weight).IsEqualTo(30);
        await Assert.That(picked[2].ParentNextEvent.Weight).IsEqualTo(0);
    }

    [Test]
    public async Task SingleWeightedSibling_IsAlwaysDrawn()
    {
        var children = new List<PlotNode> { Child(0), Child(7) };
        for (var ticket = 0; ticket < 7; ticket++)
        {
            var picked = PlotBranchRules.SelectEligible(children, _ => ticket);
            await Assert.That(picked.Count).IsEqualTo(2);
            await Assert.That(picked[1]).IsSameReferenceAs(children[1]);
        }
    }

    [Test]
    public async Task SelectChildren_TakesTheEdgeTheConditionSelects()
    {
        var children = new List<PlotNode> { Child(10, fail: false), Child(20, fail: true), Child(0, fail: false) };

        var onSuccess = PlotBranchRules.SelectChildren(children, condition: true, _ => 0);
        await Assert.That(onSuccess.Count).IsEqualTo(2);
        await Assert.That(onSuccess[0].ParentNextEvent.Fail).IsFalse();
        await Assert.That(onSuccess[1].ParentNextEvent.Fail).IsFalse();

        var onFail = PlotBranchRules.SelectChildren(children, condition: false, _ => 0);
        await Assert.That(onFail.Count).IsEqualTo(1);
        await Assert.That(onFail[0]).IsSameReferenceAs(children[1]);
    }

    [Test]
    public async Task TicketOutsideThePool_IsClampedInsteadOfThrowing()
    {
        var children = new List<PlotNode> { Child(3), Child(1) };

        foreach (var ticket in new[] { -100, 0, 3, 1_000_000 })
        {
            var picked = PlotBranchRules.SelectEligible(children, _ => ticket);
            await Assert.That(picked.Count).IsEqualTo(1);
        }

        // Above the pool the ticket lands on the last ticket in the pool, never past the end.
        var last = PlotBranchRules.SelectEligible(children, _ => int.MaxValue);
        await Assert.That(last[0]).IsSameReferenceAs(children[1]);
    }

    [Test]
    public async Task EmptyCandidateSet_SelectsNothing()
    {
        await Assert.That(PlotBranchRules.SelectEligible([], _ => 0).Count).IsEqualTo(0);
        await Assert.That(PlotBranchRules.SelectChildren([], true, _ => 0).Count).IsEqualTo(0);
    }
}
