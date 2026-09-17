using AAEmu.Game.Utils;


namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The shape of a skill's area: the cone <c>target_area_angle</c> / <c>front_angle</c> describe, the
/// corridor a <c>Line</c> selection describes, and where the area is gathered.
/// </summary>
/// <remarks>
/// The gather used to be a full sphere around the resolved target: <c>target_area_angle</c> was loaded and
/// never read, so a 90° cleave hit everything within <c>target_area_radius</c>, including units behind the
/// caster. 1,364 skills carry an angle below the 360 default; 33 of them are ability skills.
///
/// <c>target_area_angle</c> is read as a half-angle, matching what <c>AreaShape.FilterSphereCone</c>
/// already does with <c>aoe_shapes.value3</c> and the historical reading that file documents. 360 is "no
/// cone" and is skipped rather than halving to 180. The same open question applies to both columns — see
/// the <c>SphereConeValue3IsFullSweep</c> note in <c>AreaShape</c>, which is F6's to settle — and reading
/// it wider rather than narrower keeps an AoE hitting what it hits today.
///
/// <c>front_angle</c> only applies when <c>target_area_angle</c> is the 360 default: 39 skills carry both,
/// and the area angle is the more specific statement.
///
/// <c>target_selection_id</c> is 1 (Source) on 25,912 skills, 2 (Target) on 11,802 and 4 (Location) on 329.
/// No shipped row uses 3 (Line); the corridor rule is written anyway so that a row which does use it is
/// narrowed rather than silently treated as a full sphere. Source and Target keep the gather they have —
/// changing which unit 25,912 skills centre on is a live-behaviour change this task does not ask for and
/// no evidence in the content settles.
/// </remarks>
public static class SkillAreaRules
{
    /// <summary>The angle meaning "every bearing", which is also the DB default.</summary>
    public const int FullSweepDegrees = 360;

    /// <summary>
    /// The cone's half-angle in degrees, or 0 when the skill has no cone.
    /// </summary>
    public static double ConeHalfAngle(int targetAreaAngle, int frontAngle)
    {
        if (targetAreaAngle > 0 && targetAreaAngle < FullSweepDegrees)
            return targetAreaAngle;
        if (frontAngle > 0 && frontAngle < FullSweepDegrees)
            return frontAngle;
        return 0d;
    }

    /// <summary>
    /// Whether a bearing from the caster's facing is inside the cone. <paramref name="bearing"/> is
    /// <see cref="MathUtil.CalculateAngleFrom(Units.BaseUnit, Units.BaseUnit)"/>'s signed result, so a
    /// unit behind the caster reads near ±180.
    /// </summary>
    public static bool IsInsideCone(double bearing, double halfAngle)
        => halfAngle <= 0d || Math.Abs(MathUtil.ClampDegAngle(bearing)) <= halfAngle;

    /// <summary>
    /// Whether a <c>Line</c> selection reaches a unit: within <paramref name="halfWidth"/> of the segment
    /// from the caster to the cast target, using the X/Y plane the rest of the AoE code works in. The
    /// segment is capped at both ends, so the shape is a stadium rather than an infinite band: a unit
    /// beside the caster or beside the target is inside, one past either end is not.
    /// </summary>
    public static bool IsWithinCorridor(
        (float X, float Y) caster, (float X, float Y) target, (float X, float Y) unit, double halfWidth)
    {
        if (halfWidth <= 0d)
            return false;

        var segmentX = target.X - caster.X;
        var segmentY = target.Y - caster.Y;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;

        // A degenerate segment (the cast target is the caster) has no line to be near.
        if (lengthSquared <= float.Epsilon)
            return false;

        var projection = ((unit.X - caster.X) * segmentX + (unit.Y - caster.Y) * segmentY) / lengthSquared;
        // Only the part of the line between the caster and the target counts: past the target the area
        // stops, and behind the caster it never started.
        var clamped = Math.Clamp(projection, 0f, 1f);
        var closestX = caster.X + clamped * segmentX;
        var closestY = caster.Y + clamped * segmentY;
        var distanceX = unit.X - closestX;
        var distanceY = unit.Y - closestY;

        return distanceX * distanceX + distanceY * distanceY <= halfWidth * halfWidth;
    }

    /// <summary>
    /// Whether a selection gathers around the position the cast was aimed at rather than around the
    /// resolved unit. A <c>Location</c> cast is already resolved to a position pseudo-unit, so this only
    /// records the intent; <c>Line</c> narrows to the corridor instead.
    /// </summary>
    public static bool GathersAroundCastPosition(SkillTargetSelection selection)
        => selection == SkillTargetSelection.Location;

    /// <summary>Whether a selection narrows the gathered set to the caster-to-target corridor.</summary>
    public static bool UsesCorridor(SkillTargetSelection selection)
        => selection == SkillTargetSelection.Line;
}
