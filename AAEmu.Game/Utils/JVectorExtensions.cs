using Jitter2.LinearMath;

namespace AAEmu.Game.Utils;

/// <summary>
/// Helpers for <see cref="JVector"/> that <see cref="System.Numerics.Vector3"/> has and Jitter does not.
/// Note that <c>JVector * JVector</c> is a dot product, while <c>Vector3 * Vector3</c> multiplies component-wise.
/// </summary>
public static class JVectorExtensions
{
    /// <summary>
    /// Distance between two points.
    /// </summary>
    public static float Distance(this JVector a, JVector b)
    {
        return (a - b).Length();
    }

    /// <summary>
    /// Squared distance between two points (cheaper than <see cref="Distance"/> for comparisons).
    /// </summary>
    public static float DistanceSquared(this JVector a, JVector b)
    {
        return (a - b).LengthSquared();
    }
}
