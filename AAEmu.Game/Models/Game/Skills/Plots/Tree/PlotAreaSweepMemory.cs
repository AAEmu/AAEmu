using System.Runtime.CompilerServices;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// The position a repeating area step occupied before it slid, kept on the stand the next search reads.
/// </summary>
internal static class PlotAreaSweepMemory
{
    private sealed class Mark
    {
        public float X;
        public float Y;
    }

    private static readonly ConditionalWeakTable<BaseUnit, Mark> Marks = new();

    public static void Remember(BaseUnit stand, float fromX, float fromY)
    {
        if (stand == null)
            return;

        Marks.Remove(stand);
        Marks.Add(stand, new Mark { X = fromX, Y = fromY });
    }

    public static bool TryGet(BaseUnit stand, out float fromX, out float fromY)
    {
        if (stand != null && Marks.TryGetValue(stand, out var mark))
        {
            fromX = mark.X;
            fromY = mark.Y;
            return true;
        }

        fromX = 0f;
        fromY = 0f;
        return false;
    }
}
