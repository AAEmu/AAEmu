using System.Numerics;

namespace AAEmu.Game.Models.Game.Skills.Plots;

/// <summary>
/// Wire form of plot POSITION <c>rot.x/y/z</c> on <c>SCPlotEvent</c>.
/// </summary>
/// <remarks>
/// Those three bytes are not Euler yaw. The client compresses the pose quaternion to axis-angle:
/// each component is <c>axis[i] * (angle / 2π) * 127</c> as an sbyte. Encode matches the client
/// writer that fills <c>rot.*</c>; decode matches the reader that rebuilds the quaternion.
/// Writing the 128-step Euler facing byte here made SE (~225°) land on +80 while the client
/// expected ~−48, so the telegraph aimed sideways until yaw crossed into a band where the two
/// encodings accidentally agreed.
/// </remarks>
public static class PlotPoseRotationWire
{
    private const float TwoPi = MathF.PI * 2f;
    private const float InvTwoPi = 1f / TwoPi;
    private const float SbyteMax = 127f;
    private const float MinAngle = 1e-4f;
    private const float MinAxisLenSq = 1.1920929e-7f;

    /// <summary>Compress a unit quaternion to the three plot-pose rotation bytes.</summary>
    public static (sbyte X, sbyte Y, sbyte Z) Encode(Quaternion q)
    {
        // Prefer the short-arc representative (W >= 0). A Transform yaw stored as 236° builds the
        // same orientation as −124°, but the long-arc form would emit +83 instead of −44 — the
        // byte the live SE miss window needed.
        if (q.W < 0f)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);

        var w = Math.Clamp(q.W, -1f, 1f);
        var angle = MathF.Acos(w) * 2f;
        if (angle <= MinAngle)
            return (0, 0, 0);

        var halfSin = MathF.Sin(angle * 0.5f);
        if (MathF.Abs(halfSin) < 1e-8f)
            return (0, 0, 0);

        var invHalfSin = 1f / halfSin;
        var ax = q.X * invHalfSin;
        var ay = q.Y * invHalfSin;
        var az = q.Z * invHalfSin;
        var lenSq = ax * ax + ay * ay + az * az;
        if (lenSq <= MinAxisLenSq)
            return (0, 0, 0);

        var invLen = 1f / MathF.Sqrt(lenSq);
        ax *= invLen;
        ay *= invLen;
        az *= invLen;

        var scale = angle * InvTwoPi;
        return (ToSByte(ax * scale), ToSByte(ay * scale), ToSByte(az * scale));
    }

    /// <summary>Expand plot-pose rotation bytes back to a unit quaternion.</summary>
    public static Quaternion Decode(sbyte x, sbyte y, sbyte z)
    {
        var fx = x / SbyteMax;
        var fy = y / SbyteMax;
        var fz = z / SbyteMax;
        // Client compares the sum of squares (not the length) against these thresholds.
        var magSq = fx * fx + fy * fy + fz * fz;
        if (magSq > 0.99993801f || magSq < 0.000062000123f)
            return Quaternion.Identity;

        var mag = MathF.Sqrt(magSq);
        var inv = 1f / mag;
        fx *= inv;
        fy *= inv;
        fz *= inv;
        var half = mag * TwoPi * 0.5f;
        var s = MathF.Sin(half);
        var c = MathF.Cos(half);
        return new Quaternion(fx * s, fy * s, fz * s, c);
    }

    private static sbyte ToSByte(float scaledAxisComponent)
    {
        var rounded = (int)MathF.Round(scaledAxisComponent * SbyteMax, MidpointRounding.AwayFromZero);
        if (rounded > 127)
            rounded = 127;
        if (rounded < -128)
            rounded = -128;
        return (sbyte)rounded;
    }
}
