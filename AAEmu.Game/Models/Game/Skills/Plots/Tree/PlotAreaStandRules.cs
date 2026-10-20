namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// A plot area stand is cloned from the previous target and then slid. The clone keeps that
/// unit's region. A shape search walks only the region it is given, so once the slide crosses
/// a region boundary the search looks in the old cell and a unit inside the shape is missed.
/// </summary>
public static class PlotAreaStandRules
{
    public static bool StandsInRegion(int regionX, int regionY, float worldX, float worldY, int regionSize)
    {
        if (regionSize <= 0)
            return false;
        return (int)(worldX / regionSize) == regionX && (int)(worldY / regionSize) == regionY;
    }
}
