namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The pure part of <see cref="SpawnEffect"/>'s placement: where a spawn lands relative to the unit the row
/// anchors it to.
/// </summary>
/// <remarks>
/// <c>spawn_effects</c> split the old <c>pos_angle</c>/<c>pos_distance</c> pair into <c>_min</c>/<c>_max</c>
/// in 10.0.2.13. The loader used to keep only the minimum, so 237 angle rows and 248 distance rows — every
/// row whose two ends differ — placed every summon at the same spot. A row whose ends match is exact and
/// behaves exactly as it did.
/// </remarks>
public static class SpawnScatterRules
{
    /// <summary>
    /// A value drawn from <c>[min, max]</c> inclusive.
    /// </summary>
    /// <remarks>
    /// <paramref name="roll"/> is 0..1000 from the caller so the draw is testable. Both ends are reachable:
    /// a row authored 0-360 (mate effects 3438/3439) covers the whole circle, and rounding keeps the
    /// degrees the table authors — these are whole-degree columns.
    /// </remarks>
    public static float Scatter(float min, float max, int roll)
    {
        if (max <= min)
            return min;

        var fraction = Math.Clamp(roll, 0, 1000) / 1000f;
        return min + (max - min) * fraction;
    }

    /// <summary>
    /// Whether a row's two ends differ, which is what "this spawn scatters" means.
    /// </summary>
    public static bool Scatters(float min, float max) => max > min;

    /// <summary>
    /// The angle a row places its spawn at, in degrees.
    /// </summary>
    public static float Angle(float angleMin, float angleMax, int roll) => Scatter(angleMin, angleMax, roll);

    /// <summary>
    /// The distance a row places its spawn at.
    /// </summary>
    public static float Distance(float distanceMin, float distanceMax, int roll) => Scatter(distanceMin, distanceMax, roll);

    /// <summary>
    /// The Z a row's ray cast starts from: the anchor's own Z raised by <c>ray_off_set</c>.
    /// </summary>
    /// <remarks>
    /// <c>ray_off_set</c> (548 rows non-zero; 50 on 183, 5 on 135, 10 on 74) is the only unread column left
    /// in <c>spawn_effects</c> and sits next to <c>enable_ray_cast</c>, so it is read as the height the cast
    /// is taken from — the summon drops that far onto the ground under the anchor. The values fit that: they
    /// are metres of head-room, and every row carrying one also leaves <c>enable_ray_cast</c> true except
    /// mate effect 2827. It is an inference from the column name and its neighbours, not something the
    /// 10.0.2.13 client can be asked about, so it is applied as a plain offset and everything downstream
    /// (the terrain snap, the flier check) is unchanged.
    /// </remarks>
    public static float RayCastOriginZ(float anchorZ, float rayOffSet) => anchorZ + rayOffSet;
}
