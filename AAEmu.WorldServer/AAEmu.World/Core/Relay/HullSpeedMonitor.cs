using System.Collections.Concurrent;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Measures how fast a zone-simulated hull actually travels, from the positions it reports rather
/// than from the quantised velocity in the movement body.
/// </summary>
/// <remarks>
/// A hull cannot drive itself past its <c>ship_models.velocity</c>: the thrust a zone applies is
/// scaled by the headroom left to that speed and is zero once the hull reaches it. So a hull moving
/// faster than its own maximum was given the speed from outside the throttle, and World cannot take
/// it back — a zone that owns a hull ignores the poses World sends it. Measuring the travelled
/// distance is what separates "a fast ship" from "a hull that was thrown", and the speed is kept on
/// the hull so the next zone is not asked to continue an impossible one.
/// </remarks>
public static class HullSpeedMonitor
{
    /// <summary>A hull is reported once it exceeds its maximum by this much, to leave the cap alone.</summary>
    public const float OverspeedFactor = 1.25f;

    /// <summary>Samples closer together than this measure quantisation noise instead of travel.</summary>
    public const long MinSampleMs = 100;

    /// <summary>Samples further apart than this span a gap (a handoff, a stall) and are not travel.</summary>
    public const long MaxSampleMs = 3000;

    private const long ReportIntervalMs = 2000;

    private static readonly ConcurrentDictionary<uint, Sample> Samples = new();
    private static readonly ConcurrentDictionary<uint, long> Reported = new();

    private readonly record struct Sample(uint ZoneId, float X, float Y, float Z, long AtMs);

    /// <summary>
    /// Records a hull position reported by the zone simulating it.
    /// </summary>
    /// <param name="zoneId">
    /// The zone that reported it. A hull handed to another simulator starts a new measurement, because
    /// the two do not agree to the metre and the step between them is not travel.
    /// </param>
    /// <returns>
    /// Speed in metres per second, or null when the previous sample is missing, belongs to another
    /// simulator, is too fresh to measure, or is too old to be one continuous stretch of sailing.
    /// </returns>
    public static float? Observe(uint bcId, uint zoneId, float x, float y, float z, long nowMs)
    {
        if (!Samples.TryGetValue(bcId, out var from) || from.ZoneId != zoneId)
        {
            Samples[bcId] = new Sample(zoneId, x, y, z, nowMs);
            return null;
        }

        // The baseline is only replaced once it is old enough to measure against. Advancing it on every
        // report made the gap between samples the report interval itself — a simulator publishes a hull
        // roughly every 50 ms, always short of the floor below, so no pair was ever measurable and this
        // never returned a speed at all.
        var elapsed = nowMs - from.AtMs;
        if (elapsed < MinSampleMs)
            return null;

        Samples[bcId] = new Sample(zoneId, x, y, z, nowMs);

        if (elapsed > MaxSampleMs)
            return null;

        // Speed made good, measured horizontally. A hull on water rides the surface up and down without
        // getting anywhere, so counting the vertical component reports wave motion as travel and reads
        // high — which then has to be discounted again by whatever consumes the figure.
        var dx = x - from.X;
        var dy = y - from.Y;
        return (float)(System.Math.Sqrt((dx * dx) + (dy * dy)) / (elapsed / 1000d));
    }

    /// <summary>Whether a measured speed is beyond what the hull can reach under its own thrust.</summary>
    public static bool IsOverspeed(float speed, float maxVelocity) =>
        maxVelocity > 0f && speed > maxVelocity * OverspeedFactor;

    /// <summary>Rate limits the report so a hull that stays too fast does not fill the log.</summary>
    public static bool ShouldReport(uint bcId, long nowMs)
    {
        if (Reported.TryGetValue(bcId, out var last) && nowMs - last < ReportIntervalMs)
            return false;

        Reported[bcId] = nowMs;
        return true;
    }

    /// <summary>Drops the hull's history, so the zone it moves to does not measure across the move.</summary>
    public static void Forget(uint bcId)
    {
        Samples.TryRemove(bcId, out _);
        Reported.TryRemove(bcId, out _);
    }
}
