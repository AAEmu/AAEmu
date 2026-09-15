namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Whether a plot can be executed at all, and what has to happen when it cannot.
/// </summary>
/// <remarks>
/// 67 of the 10.0.2.13 plots have no <c>position = 1</c> row in <c>plot_events</c>, so
/// <c>PlotManager</c> never calls <c>PlotBuilder.BuildTree</c> for them and
/// <see cref="Plots.Plot.Tree"/> stays null — while 30 skills still point at those plots (13499 → plot 47,
/// 16728-16745 → plots 283-300, 19247 → 553, and so on). <see cref="Plots.Plot.RunAsync"/> used to walk
/// straight into the null tree; because every call site starts the plot from <c>Task.Run</c>, the
/// NullReferenceException surfaced only as an unobserved task exception and the rest of the plot-end
/// bookkeeping never ran.
/// </remarks>
public static class PlotEndRules
{
    /// <summary>A tree is runnable once <c>PlotBuilder</c> has given it a root node.</summary>
    public static bool HasRunnableTree(PlotTree tree) => tree?.RootNode != null;

    /// <summary>
    /// A plot with nothing to execute still has to end the skill: the cooldown, the TlId release, the
    /// zone relay and both <c>ActivePlotState</c> fields are owned by the plot-end sequence, not by the
    /// tree, so the skill must be ended rather than left running.
    /// </summary>
    public static bool ShouldEndWithoutTree(PlotTree tree) => !HasRunnableTree(tree);
}
