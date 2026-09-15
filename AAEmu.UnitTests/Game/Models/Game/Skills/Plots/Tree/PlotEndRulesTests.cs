using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// 10.0.2.13 ships 67 plots with no <c>position = 1</c> row in <c>plot_events</c>, so
/// <c>PlotManager.Load</c> never builds a tree for them — and 30 skills still cast them (13499 → plot 47,
/// 16728-16745 → plots 283-300, 19247 → 553). Those casts have to end the skill, not run a null tree.
/// </summary>
public class PlotEndRulesTests
{
    [Test]
    public async Task ShouldEndWithoutTree_NoTreeAtAll_IsTrue()
    {
        await Assert.That(PlotEndRules.ShouldEndWithoutTree(null)).IsTrue();
    }

    [Test]
    public async Task ShouldEndWithoutTree_TreeWithoutRootNode_IsTrue()
    {
        await Assert.That(PlotEndRules.ShouldEndWithoutTree(new PlotTree(47))).IsTrue();
    }

    [Test]
    public async Task ShouldEndWithoutTree_TreeWithRootNode_IsFalse()
    {
        var tree = new PlotTree(47);
        tree.RootNode = new PlotNode { Tree = tree };

        await Assert.That(PlotEndRules.ShouldEndWithoutTree(tree)).IsFalse();
        await Assert.That(PlotEndRules.HasRunnableTree(tree)).IsTrue();
    }

    [Test]
    public async Task EndPlotWithoutTree_NoState_DoesNotThrow()
    {
        Exception error = null;
        try
        {
            PlotTree.EndPlotWithoutTree(null);
        }
        catch (Exception e)
        {
            error = e;
        }

        await Assert.That(error).IsNull();
    }
}
