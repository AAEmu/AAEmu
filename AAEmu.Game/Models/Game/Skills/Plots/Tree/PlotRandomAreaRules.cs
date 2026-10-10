namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Where a plot RandomArea marker is placed relative to its previous target.
/// </summary>
/// <remarks>
/// <c>target_update_method_param3</c> is a radius in millimetres, not a fixed ring distance. Using the
/// full value every time put every Gods' Whip Wave bolt (39674 / plot 3778) on a 4 m circumference
/// while each bolt's damage box is only 2×2 m — the aim point (and anything standing on it) sat
/// outside every hit volume, so the skill painted lightning and dealt nothing.
/// </remarks>
public static class PlotRandomAreaRules
{
    /// <summary>
    /// Horizontal offset in metres for a uniform pick inside a disk of radius
    /// <paramref name="distanceMm"/> / 1000. <paramref name="unitSample01"/> is a unit random in
    /// [0, 1]; yaw is chosen separately by the caller.
    /// </summary>
    public static float OffsetMetres(int distanceMm, float unitSample01)
    {
        if (distanceMm <= 0)
            return 0f;

        var radius = distanceMm / 1000f;
        var u = Math.Clamp(unitSample01, 0f, 1f);
        // sqrt(U) keeps area density uniform across the disk.
        return radius * MathF.Sqrt(u);
    }
}
