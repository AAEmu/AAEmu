using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Advances a last-reported hull pose by the way it was making, so a zone enter is not planted
/// where the boat was when the report was taken.
/// </summary>
/// <remarks>
/// Create and the type-4 seed copy the outgoing simulator's last body. That body is already old
/// by the time the incoming zone consumes it (hysteresis, the physicalize wait, then the overlap
/// while World still follows the old simulator). Planting the enter snapshot makes the new zone
/// restore at that xyz after the client has already continued to xyz + v·dt. The correction is
/// the last continent position plus the last reported velocity times how late that pose is when
/// follow actually switches — not a new packet, and not an invented offset.
/// </remarks>
public static class BoatSeamPredictRules
{
    /// <summary>
    /// Same window the seam impulse already treats as "this speed is no longer the hull".
    /// A pose older than that is not extrapolated.
    /// </summary>
    public static long MaxPredictAgeMs => BoatSeamImpulse.FreshnessWindowMs;

    /// <summary>
    /// Extra plant beyond the physicalize wait. Always zero: follow switches on the first
    /// consumed-body report from the incoming zone (~arm delay), so planting a second of
    /// overlap ahead leaves that body sitting at a future xyz while the old simulator is
    /// still followed.
    /// </summary>
    public static long OverlapAheadMs(bool seamOverlap, float measuredSpeed, long speedAgeMs, sbyte liveThrottle)
    {
        _ = (seamOverlap, measuredSpeed, speedAgeMs, liveThrottle);
        return 0;
    }

    /// <summary>
    /// A type-4 body can report throttle 0 for one frame at a seam while the rider still holds way.
    /// </summary>
    public static sbyte LiveThrottle(sbyte reported, sbyte request, sbyte applied)
    {
        if (reported != 0)
            return reported;
        if (request != 0)
            return request;
        return applied;
    }

    /// <summary>
    /// How far ahead of the last report to place the hull, in milliseconds.
    /// </summary>
    public static long AheadMs(long poseAgeMs, long extraAheadMs)
    {
        if (poseAgeMs < 0 || poseAgeMs >= MaxPredictAgeMs)
            return 0;

        extraAheadMs = Math.Max(0, extraAheadMs);
        var total = poseAgeMs + extraAheadMs;
        return total > MaxPredictAgeMs ? MaxPredictAgeMs : total;
    }

    public static float DecodeVelMetresPerSecond(short quantised) =>
        quantised / (float)short.MaxValue * ShipMoveType.VelocityQuantizationScale;

    /// <summary>
    /// Continent XYZ the incoming zone should receive: last report plus velocity × ahead.
    /// </summary>
    public static (float X, float Y, float Z) Advance(
        float x, float y, float z,
        short velX, short velY, short velZ,
        long aheadMs)
    {
        if (aheadMs <= 0)
            return (x, y, z);

        var dt = aheadMs / 1000f;
        return (
            x + DecodeVelMetresPerSecond(velX) * dt,
            y + DecodeVelMetresPerSecond(velY) * dt,
            z + DecodeVelMetresPerSecond(velZ) * dt);
    }
}
