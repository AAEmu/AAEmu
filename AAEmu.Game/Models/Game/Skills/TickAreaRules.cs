using System.Numerics;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The shape of a buff's tick area: <c>buffs.tick_area_angle</c>, <c>buffs.tick_area_front_angle</c> and
/// <c>buffs.tick_area_max_count</c>. Before this class <c>BuffTemplate.DoAreaTick</c> gathered everything
/// inside <c>tick_area_radius</c> and ignored all three, so a cone in front of the caster hit units behind
/// it and a tick the content caps at 30 hit the whole camp.
/// </summary>
/// <remarks>
/// Column readings, counted against the 10.0.2.13 content DB (30 654 <c>buffs</c> rows, 603 with a tick
/// radius):
/// <list type="bullet">
/// <item><c>tick_area_angle</c> is the sweep of the wedge in degrees, and 360 is the shipped "all
/// around": 536 of the 603 radius rows sit at exactly 360 and 3 more at 0 (unset), so only 64 rows are
/// narrower than a full circle — 40° on 24 of them, 20° on 13, 90° on 9, 120° on 9, 180° on 2, and 25/30/50/75
/// on the rest. Only the 64 get a wedge; 0 and 360 both mean "no wedge", which is what keeps the other
/// 539 rows exactly as they were.</item>
/// <item><c>tick_area_front_angle</c> is a second wedge of the same shape, carried by 2 rows (16804 at
/// 120 with <c>tick_area_angle</c> 120, 26567 at 180 with <c>tick_area_angle</c> 360 — a half circle in
/// front of the caster). Both are applied; a row with neither is untouched.</item>
/// <item><c>tick_area_max_count</c> caps one tick at the nearest N units: 122 radius rows carry one
/// (1 on buff 24611, 10 on 23545, 30 on the 경쾌한 행진곡 songs, 200 on 26099).</item>
/// </list>
/// The wedge is measured the way <c>AreaShape.FilterSphereCone</c> measures one — the bearing from the
/// origin's world facing, which <c>MathUtil.CalculateAngleFrom</c> subtracts the origin's yaw to get — so
/// a tick area and a skill's sphere cone agree about where "in front" is. The sweep is handed to the shape
/// unchanged, so the column is read the same way <c>aoe_shapes.value3</c> is: as the full width of the
/// wedge, 120 meaning ±60. The shape code settles that reading in <c>AreaShape.SphereConeValue3IsFullSweep</c>,
/// and the two 180 rows are a half circle rather than a no-op.
/// </remarks>
public static class TickAreaRules
{
    /// <summary>
    /// The wedge <paramref name="angle"/> describes, in degrees: 0 when the column is unset or when the
    /// wedge is the full circle, which is the same "no filter" answer the caller wants for both.
    /// </summary>
    public static float ConeSweep(int angle) => angle > 0 && angle < 360 ? angle : 0f;

    /// <summary>Whether either wedge column asks for a filter.</summary>
    public static bool HasCone(int angle, int frontAngle) =>
        ConeSweep(angle) > 0f || ConeSweep(frontAngle) > 0f;

    /// <summary>The cap <paramref name="maxCount"/> describes; 0 and negatives mean "no cap".</summary>
    public static int MaxTargets(int maxCount) => maxCount > 0 ? maxCount : 0;

    /// <summary>
    /// Applies both wedges and then the cap to a gathered tick area. Every step is a no-op on the content
    /// that does not set its column: 539 of the 603 tick radii, and 481 of them for the cap.
    /// </summary>
    public static List<T> Apply<T>(GameObject origin, List<T> units, int angle, int frontAngle, int maxCount)
        where T : GameObject
    {
        if (units == null || units.Count == 0)
            return units ?? [];

        var result = units;
        var sweep = ConeSweep(angle);
        if (sweep > 0f)
            result = InCone(origin, result, sweep);
        sweep = ConeSweep(frontAngle);
        if (sweep > 0f)
            result = InCone(origin, result, sweep);

        return Nearest(origin, result, MaxTargets(maxCount));
    }

    /// <summary>
    /// Keeps the units whose bearing from <paramref name="origin"/>'s facing is within
    /// ±<paramref name="sweep"/>/2 degrees — <paramref name="sweep"/> is the full width of the wedge,
    /// the way <c>AreaShape.Value3</c> carries it.
    /// </summary>
    public static List<T> InCone<T>(GameObject origin, List<T> units, float sweep) where T : GameObject
    {
        if (units == null || units.Count == 0)
            return units ?? [];

        // No origin to face from — a tick whose owner is not a Unit. The wedge cannot be evaluated, and
        // dropping every target would be a far bigger change than leaving the list alone.
        if (origin?.Transform == null || sweep <= 0f)
            return units;

        return new AreaShape { Type = AreaShapeType.Sphere, Value3 = sweep }.FilterSphereCone(origin, units);
    }

    /// <summary>
    /// Keeps the <paramref name="maxCount"/> units nearest <paramref name="origin"/> when the tick area
    /// holds more of them. The gather order is the world's, so it is only sorted when a cap applies.
    /// </summary>
    public static List<T> Nearest<T>(GameObject origin, List<T> units, int maxCount) where T : GameObject
    {
        if (units == null || units.Count == 0)
            return units ?? [];

        if (maxCount <= 0 || units.Count <= maxCount || origin?.Transform == null)
            return units;

        var originPosition = origin.Transform.World.Position;
        return units
            .OrderBy(unit => DistanceSquared(originPosition, unit.Transform.World.Position))
            .Take(maxCount)
            .ToList();
    }

    private static float DistanceSquared(Vector3 from, Vector3 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
