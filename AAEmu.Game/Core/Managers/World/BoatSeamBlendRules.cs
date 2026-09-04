namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Streams a continuous hull path across a follow switch. Two simulators never agree exactly on
/// where the hull is: after the catch-up the incoming body is still 0.3–0.9 m off the track the
/// client was watching (cross-track in a turn, a sample hole and a heave settle on a straight
/// crossing). The bodies written to clients for the first <see cref="BlendMs"/> after the switch
/// start from the outgoing body's track and converge onto the incoming body, so the client
/// interpolates one path instead of stepping between two.
/// </summary>
/// <remarks>
/// Only the position and yaw written to clients are blended. The World mirror and every seed
/// keep the zone's own report.
/// </remarks>
public static class BoatSeamBlendRules
{
    /// <summary>
    /// Window over which the residual offset is worked off. Same window as the catch-up pulse:
    /// a sub-metre offset over it is a fraction of cruise in apparent velocity.
    /// </summary>
    public static long BlendMs => (long)(BoatZoneSimRules.CatchUpSeconds * 1000f);

    /// <summary>
    /// Offsets larger than this are not a seam residual but a different plant (or a hull that
    /// teleported); blending them would drag the client through open water. Twice the catch-up
    /// tolerance plus the largest residual seen live (0.9 m) rounds to this.
    /// </summary>
    public const float MaxBlendMetres = 2.5f;

    public readonly record struct Offset(float X, float Y, float Z, float YawDegrees);

    /// <summary>
    /// Residual between where the outgoing track was at this instant and the incoming body.
    /// Null when there is nothing worth blending.
    /// </summary>
    public static Offset? Residual(
        float fromX, float fromY, float fromZ, float fromYaw,
        float toX, float toY, float toZ, float toYaw)
    {
        var dx = fromX - toX;
        var dy = fromY - toY;
        var dz = fromZ - toZ;
        var planar = MathF.Sqrt(dx * dx + dy * dy);
        if (planar > MaxBlendMetres || MathF.Abs(dz) > MaxBlendMetres)
            return null;
        if (planar < 0.01f && MathF.Abs(dz) < 0.01f && MathF.Abs(WrapDegrees(fromYaw - toYaw)) < 0.1f)
            return null;
        return new Offset(dx, dy, dz, WrapDegrees(fromYaw - toYaw));
    }

    /// <summary>Fraction of the residual still applied <paramref name="ageMs"/> after the switch.</summary>
    public static float Weight(long ageMs)
    {
        if (ageMs <= 0)
            return 1f;
        if (ageMs >= BlendMs)
            return 0f;
        return 1f - ageMs / (float)BlendMs;
    }

    public static bool IsActive(long ageMs) => ageMs >= 0 && ageMs < BlendMs;

    public static float WrapDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f)
            degrees -= 360f;
        if (degrees < -180f)
            degrees += 360f;
        return degrees;
    }
}
