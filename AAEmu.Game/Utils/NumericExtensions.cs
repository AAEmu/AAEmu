using System.Numerics;
using Jitter.LinearMath;

namespace AAEmu.Game.Utils;

public static class NumericExtensions
{
    public static double DegToRad(this double val)
    {
        return (Math.PI / 180f) * val;
    }

    public static float DegToRad(this float val)
    {
        return (MathF.PI / 180f) * val;
    }

    public static double RadToDeg(this double val)
    {
        return val / Math.PI * 180f;
    }

    public static float RadToDeg(this float val)
    {
        return val / MathF.PI * 180f;
    }

    public static Vector3 JVectorToVector(this JVector val)
    {
        return new Vector3(val.X, val.Z, val.Y);
    }

    public static JVector VectorToJVector(this Vector3 val)
    {
        return new JVector(val.X, val.Z, val.Y);
    }
}
