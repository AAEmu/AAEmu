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
            PlotTree.DropPlotState(null);
        }
        catch (Exception e)
        {
            error = e;
        }

        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task OwnsSkillEnd_OnlyForPlotOnlyAndForcedGraphs()
    {
        // 13499 and 36858 are the only casters of a treeless plot that are plot_only; every other one
        // returns from Skill.Use into a cast that ends the skill itself.
        await Assert.That(PlotEndRules.OwnsSkillEnd(plotOnly: true, forcePlotGraphOnly: false)).IsTrue();
        // The hold/reel kit ships a plot without plot_only but sets ForcePlotGraphOnly (Skill.cs:306).
        await Assert.That(PlotEndRules.OwnsSkillEnd(plotOnly: false, forcePlotGraphOnly: true)).IsTrue();
        await Assert.That(PlotEndRules.OwnsSkillEnd(plotOnly: false, forcePlotGraphOnly: false)).IsFalse();
    }
}
