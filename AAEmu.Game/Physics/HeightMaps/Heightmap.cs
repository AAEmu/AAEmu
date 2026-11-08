using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.HeightMaps;

public class Heightmap(float[,] heights)
{
    public float[,] RawHeights { get; init; } = heights;
    public int Width => RawHeights.GetLength(0);
    public int Height => RawHeights.GetLength(1);
    public float MinHeight { get; } = heights.Cast<float>().Min();
    public float MaxHeight { get; } = heights.Cast<float>().Max();

    public float GetHeight(int x, int y) => RawHeights[x / 2, y / 2];

    public JBoundingBox GetBoundingBox()
    {
        var min = new JVector(0, 0, MinHeight);
        var max = new JVector(Width - 1, Height - 1, MaxHeight);
        return new JBoundingBox(min, max);
    }
}
