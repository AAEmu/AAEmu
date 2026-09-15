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

    /// <summary>
    /// Whether the plot owns the end of the skill it was started for. Only <c>plot_only</c> skills (and
    /// the hold/reel kit, which sets <c>ForcePlotGraphOnly</c>) return from <c>Skill.Use</c> without an
    /// <c>EndSkill</c> behind them — every other skill starts its plot from <c>Task.Run</c> and then
    /// carries on to cast, fire and end the skill itself.
    /// </summary>
    /// <remarks>
    /// 28 of the 30 skills that cast a treeless plot are <c>plot_only = 'f'</c>: ending those from here
    /// would arm the cooldown, broadcast <c>SCPlotEnded</c> and release the TlId from under the cast that
    /// is still running, and every later packet of that cast would carry TlId 0. Only 13499 and 36858
    /// own their skill end.
    /// </remarks>
    public static bool OwnsSkillEnd(bool plotOnly, bool forcePlotGraphOnly) => plotOnly || forcePlotGraphOnly;
}
