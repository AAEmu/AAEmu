namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// What the client is allowed to see on a type-4 ship body at a zone seam.
/// </summary>
/// <remarks>
/// The rudder joint is driven by the type-4 steering byte. The same body also carries a zone id
/// the client uses as an interpolator reset: a new id dumps the queue and the rudder snaps to
/// whatever the incoming simulator reports (often 0, then the stick). Follow-switch is two
/// simulators, not a single body changing zone, so the streamed id and time stay on the last
/// sample. Steering holds the last non-center value when the new body reports a centered rudder
/// while the stick is still on that side — the request byte itself is not written (it jumps
/// every helm packet and stutters the mesh).
/// </remarks>
public static class BoatRudderSeamRules
{
    /// <summary>
    /// About 12% of full throw. Below this the incoming body is treated as centered.
    /// </summary>
    public const sbyte CenterMax = 16;

    /// <param name="TimeOffset">
    /// Added to the incoming simulator's clock so the streamed clock keeps running from where the
    /// previous simulator left it. Zero while the first simulator streams.
    /// </param>
    public readonly record struct StreamedShipVisual(ushort ZoneId, uint Time, sbyte Steering, uint TimeOffset = 0);

    public static ushort PinnedZoneId(ushort lastStreamed, ushort incoming) =>
        lastStreamed != 0 ? lastStreamed : incoming;

    /// <summary>
    /// The client interpolates the hull by the body's time and drops a sample older than the last
    /// one within a second. Each simulator stamps its own clock (ms since its arm), so the new
    /// zone's first body reads far behind the one the client already has. Clamping to
    /// <c>last + 1</c> kept the samples but handed the client one millisecond per body for as long
    /// as the new clock stayed behind (~2 min per crossing): 0.6 m per "1 ms" is what the hull, its
    /// rudder and whoever stands on it jittered on. Instead the new clock is rebased with an offset
    /// so its own deltas are preserved and the streamed clock continues by the real elapsed time.
    /// </summary>
    /// <param name="elapsedMs">Wall-clock time since the last body was streamed; used only at a rebase.</param>
    public static (uint Time, uint Offset) RebasedTime(uint lastStreamed, uint incoming, uint offset, long elapsedMs)
    {
        if (lastStreamed == 0)
            return (incoming, 0);

        var candidate = unchecked(incoming + offset);
        if (candidate > lastStreamed)
            return (candidate, offset);

        // The clock went backwards: a new simulator (or a duplicate sample). Continue the streamed
        // clock by the time that really passed and remember the shift for that simulator's clock.
        var step = (uint)Math.Clamp(elapsedMs, 1, 1000);
        var rebased = lastStreamed + step;
        return (rebased, unchecked(rebased - incoming));
    }

    public static sbyte StreamedSteering(sbyte lastStreamed, sbyte incoming, sbyte stick)
    {
        if (stick == 0 || lastStreamed == 0)
            return incoming;
        if (!SameSign(stick, lastStreamed))
            return incoming;
        if (Math.Abs(incoming) <= CenterMax && Math.Abs(lastStreamed) > CenterMax)
            return lastStreamed;
        return incoming;
    }

    public static StreamedShipVisual Pin(
        in StreamedShipVisual last, ushort zoneId, uint time, sbyte steering, sbyte stick, long elapsedMs)
    {
        var (pinnedTime, offset) = RebasedTime(last.Time, time, last.TimeOffset, elapsedMs);
        return new StreamedShipVisual(
            PinnedZoneId(last.ZoneId, zoneId),
            pinnedTime,
            StreamedSteering(last.Steering, steering, stick),
            offset);
    }

    public static bool SameSign(sbyte a, sbyte b) =>
        a > 0 && b > 0 || a < 0 && b < 0;
}
