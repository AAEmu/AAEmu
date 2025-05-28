using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Utils;

public static class NumericExtensions
{
    /// <summary>
    /// Degree to Radian
    /// </summary>
    public static double DegToRad(this double val)
    {
        return (Math.PI / 180.0) * val;
    }

    /// <summary>
    /// Degree to Radian
    /// </summary>
    public static float DegToRad(this float val)
    {
        return (MathF.PI / 180f) * val;
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
    /// Converts JVector to System.Numerics.Vector3 (XZY → XYZ)
    /// </summary>
    public static Vector3 ToVector(this JVector val)
    {
        return new Vector3(val.X, val.Z, val.Y);
    }

    /// <summary>
    /// Converts Vector3 to JVector (XYZ → XZY)
    /// </summary>
    public static JVector ToJVector(this Vector3 val)
    {
        return new JVector(val.X, val.Z, val.Y);
    }

    /// <summary>
    /// Removes height axis from JVector (set Y to zero)
    /// </summary>
    public static JVector ToJVectorFix(this JVector val)
    {
        return new JVector(val.X, 0f, val.Z);
    }

    /// <summary>
    /// Removes height axis from Vector3 (set Y to zero)
    /// </summary>
    public static JVector ToVectorFix(this Vector3 val)
    {
        return new JVector(val.X, 0f, val.Z);
    }
}
