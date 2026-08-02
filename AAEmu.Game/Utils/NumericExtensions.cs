using System.Numerics;

namespace AAEmu.Game.Utils;

public static class NumericExtensions
{
    /// <summary>
    /// Degree to Radian
    /// </summary>
    public static double DegToRad(this double val)
    {
        return Math.PI / 180.0 * val;
    }

    /// <summary>
    /// Degree to Radian
    /// </summary>
    public static float DegToRad(this float val)
    {
        return MathF.PI / 180f * val;
    }

    /// <summary>
    /// Radian to Degree
    /// </summary>
    public static double RadToDeg(this double val)
    {
        return val / Math.PI * 180.0;
    }

    /// <summary>
    /// Radian to Degree
    /// </summary>
    public static float RadToDeg(this float val)
    {
        return val / MathF.PI * 180f;
    }

    /// <summary>
    /// Converts a world position into an X Y index for cells
    /// </summary>
    /// <param name="pos"></param>
    /// <returns>(cellX, cellY)</returns>
    public static (int, int) ToCellIndex(this Vector3 pos)
    {
        return ((int)Math.Floor(pos.X / 1024), (int)Math.Floor(pos.Y / 1024));
    }

    /// <summary>
    /// Converts a world position into an X Y index for chunks (paths)
    /// </summary>
    /// <param name="pos"></param>
    /// <returns>(pathsX, pathsY)</returns>
    public static (int, int) ToPathsIndex(this Vector3 pos)
    {
        return ((int)Math.Floor(pos.X / 256), (int)Math.Floor(pos.Y / 256));
    }

    /// <summary>
    /// Converts a world position into an X Y offset within it's cell
    /// </summary>
    /// <param name="pos"></param>
    /// <returns>(offsetX, offsetY)</returns>
    public static (float, float) ToCellOffset(this Vector3 pos)
    {
        return (pos.X - (float)Math.Floor(pos.X / 1024f), pos.Y - (float)Math.Floor(pos.Y / 1024f));
    }

    /// <summary>
    /// Converts a world position into an X Y offset within it's chunk (paths)
    /// </summary>
    /// <param name="pos"></param>
    /// <returns>(offsetX, offsetY)</returns>
    public static (float, float) ToPathsOffset(this Vector3 pos)
    {
        return (pos.X - (float)Math.Floor(pos.X / 256f), pos.Y - (float)Math.Floor(pos.Y / 256f));
    }
}
