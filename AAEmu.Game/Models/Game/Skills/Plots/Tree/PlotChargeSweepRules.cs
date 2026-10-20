namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// A repeating area step samples the charge at each hop. A target beside the path, inside the
/// shape radius of the segment but outside both sample spheres, is still on the charge.
/// </summary>
public static class PlotChargeSweepRules
{
    public static bool WithinRadiusSq(
        float pointX, float pointY,
        float startX, float startY,
        float endX, float endY,
        float radiusSq)
    {
        if (radiusSq < 0f)
            return false;

        var abx = endX - startX;
        var aby = endY - startY;
        var apx = pointX - startX;
        var apy = pointY - startY;
        var ab2 = (abx * abx) + (aby * aby);
        var t = ab2 <= 0.000001f ? 0f : Math.Clamp(((apx * abx) + (apy * aby)) / ab2, 0f, 1f);
        var dx = pointX - (startX + (abx * t));
        var dy = pointY - (startY + (aby * t));
        return (dx * dx) + (dy * dy) <= radiusSq;
    }
}
