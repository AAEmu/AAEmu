namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Which of a node's eligible children a plot takes when the data weights them.
/// </summary>
/// <remarks>
/// <c>plot_next_events.weight</c> is a lottery ticket among the siblings that share one parent and one
/// fail flag. 10.0.2.13 ships 2,111 weighted rows, and 650 (parent, fail) groups hold more than one of
/// them — event 16399 of plot 3302 is 50/25/25, event 16252 carries two weighted rows beside nine
/// unweighted ones. Enqueuing every eligible child (what the tree did) ran all of those branches at once.
///
/// A weight of 0 does not mean "never": it means the row was never weighted, and it keeps firing
/// unconditionally exactly as it did before. That is what makes this neutral for the 50,760 unweighted
/// rows — with nothing weighted there is no draw at all, and the eligible set is returned untouched.
/// </remarks>
public static class PlotBranchRules
{
    /// <summary>Total of the positive weights in <paramref name="candidates"/>.</summary>
    public static int TotalWeight<T>(IReadOnlyList<T> candidates, Func<T, int> weightOf)
    {
        if (candidates == null || weightOf == null)
            return 0;

        var total = 0;
        foreach (var candidate in candidates)
        {
            var weight = weightOf(candidate);
            if (weight > 0)
                total += weight;
        }

        return total;
    }

    /// <summary>
    /// The children to run: every unweighted candidate, plus one drawn from the weighted ones.
    /// </summary>
    /// <param name="candidates">The node's eligible children, in plot position order.</param>
    /// <param name="weightOf">Reads a candidate's weight; 0 or less means "always runs".</param>
    /// <param name="rollBelow">
    /// Draws a ticket in [0, total). Only called when something is weighted, so a plot with no weights
    /// consumes no randomness — the same condition chances and dice would otherwise see a different
    /// stream than before.
    /// </param>
    public static List<T> Select<T>(IReadOnlyList<T> candidates, Func<T, int> weightOf, Func<int, int> rollBelow)
    {
        var selected = new List<T>(candidates?.Count ?? 0);
        if (candidates == null || candidates.Count == 0)
            return selected;

        var weights = new int[candidates.Count];
        var total = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var weight = weightOf(candidates[i]);
            weights[i] = weight > 0 ? weight : 0;
            total += weights[i];
        }

        if (total <= 0)
        {
            selected.AddRange(candidates);
            return selected;
        }

        var ticket = Math.Clamp(rollBelow?.Invoke(total) ?? 0, 0, total - 1);
        var picked = -1;
        var running = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (weights[i] <= 0)
                continue;
            running += weights[i];
            if (ticket < running)
            {
                picked = i;
                break;
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (weights[i] > 0 && i != picked)
                continue;
            selected.Add(candidates[i]);
        }

        return selected;
    }

    /// <summary>
    /// <see cref="Select{T}"/> over plot nodes, reading each child's weight from the edge that leads to it.
    /// </summary>
    public static List<PlotNode> SelectEligible(IReadOnlyList<PlotNode> eligible, Func<int, int> rollBelow)
        => Select(eligible, child => child?.ParentNextEvent?.Weight ?? 0, rollBelow);

    /// <summary>
    /// The children of <paramref name="children"/> that this evaluation may take, weighted-picked.
    /// </summary>
    /// <param name="condition">
    /// The parent node's own condition result. A child is eligible when it is on the edge this result
    /// selects — <c>Fail == condition</c> is the fail edge, anything else is the success edge.
    /// </param>
    public static List<PlotNode> SelectChildren(IReadOnlyList<PlotNode> children, bool condition,
        Func<int, int> rollBelow)
    {
        var eligible = new List<PlotNode>(children?.Count ?? 0);
        if (children == null)
            return eligible;

        foreach (var child in children)
        {
            if (child?.ParentNextEvent == null)
                continue;
            if (condition == child.ParentNextEvent.Fail)
                continue;
            eligible.Add(child);
        }

        return SelectEligible(eligible, rollBelow);
    }
}
