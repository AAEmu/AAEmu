using System;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Plot-bounds math for a house's garden square (the <c>housing_sizes.garden_radius</c> plot).
///
/// The plot belongs to the house, so it turns with the house yaw: the client keeps a
/// <c>WorldPosPair</c> of "placement corners" in every house record and re-renders fences when a
/// house rotates (CS 0x1A0 RotateHouse / SC 0x0FE HouseRotated exist precisely for that), so a
/// world-point test must rotate the point into the house's local frame first.
///
/// The house record establishes the corner *storage* but not the client's exact point-in-plot
/// formula, so this type implements the standard yaw rotation: world = position + R(yaw) * local,
/// local = R(-yaw) * (world - position), with R the counter-clockwise 2D rotation matrix. The yaw is
/// the house transform's Z rotation in radians — PositionAndRotation.SetZRotation(float)
/// documents the transform's yaw as radian.
/// </summary>
public static class HousingPlotGeometry
{
    /// <summary>
    /// True when the world point (x, y) lies inside the rotated garden square of the house at
    /// (houseX, houseY) with the given yaw and radius. The test is inclusive on the border.
    /// </summary>
    public static bool ContainsPoint(float gardenRadius, float yawRadians, float houseX, float houseY,
        float x, float y)
    {
        var dx = x - houseX;
        var dy = y - houseY;
        var cos = MathF.Cos(yawRadians);
        var sin = MathF.Sin(yawRadians);

        // R(-yaw) * (dx, dy): the point expressed in the house's own axes.
        var localX = dx * cos + dy * sin;
        var localY = -dx * sin + dy * cos;

        return MathF.Abs(localX) <= gardenRadius && MathF.Abs(localY) <= gardenRadius;
    }

    /// <summary>
    /// Maps a point given in the house's local plot axes (origin at the house anchor) into world
    /// space: position + R(yaw) * local. Used to place plot corners (for-sale markers), which sit
    /// at (±radius, ±radius) of the local square and therefore travel around the anchor when the
    /// house is rotated.
    /// </summary>
    public static (float X, float Y) WorldPoint(float yawRadians, float houseX, float houseY,
        float localX, float localY)
    {
        var cos = MathF.Cos(yawRadians);
        var sin = MathF.Sin(yawRadians);
        return (houseX + localX * cos - localY * sin, houseY + localX * sin + localY * cos);
    }

    /// <summary>
    /// Plots closer than this along a separating axis still count as touching. Houses snap to a 4 m
    /// lattice, so neighbours usually share an edge exactly, and float error must not turn that into
    /// an overlap.
    /// </summary>
    public const float OverlapTolerance = 0.05f;

    /// <summary>
    /// True when two rotated garden squares share area. Squares that only touch along an edge or a
    /// corner do not overlap, and a plot of radius 0 overlaps nothing.
    /// </summary>
    /// <remarks>
    /// Separating-axis test on the four edge normals of the two squares. A square of half-size r with
    /// axes u and v projects onto a unit axis n with half-length r * (|u.n| + |v.n|).
    /// </remarks>
    public static bool PlotsOverlap(float radiusA, float yawA, float ax, float ay,
        float radiusB, float yawB, float bx, float by)
    {
        if (radiusA <= 0f || radiusB <= 0f)
            return false;

        var cosA = MathF.Cos(yawA);
        var sinA = MathF.Sin(yawA);
        var cosB = MathF.Cos(yawB);
        var sinB = MathF.Sin(yawB);
        var dx = bx - ax;
        var dy = by - ay;

        return !Separates(cosA, sinA) && !Separates(-sinA, cosA)
            && !Separates(cosB, sinB) && !Separates(-sinB, cosB);

        bool Separates(float nx, float ny)
        {
            var halfA = radiusA * (MathF.Abs(cosA * nx + sinA * ny) + MathF.Abs(-sinA * nx + cosA * ny));
            var halfB = radiusB * (MathF.Abs(cosB * nx + sinB * ny) + MathF.Abs(-sinB * nx + cosB * ny));
            return MathF.Abs(dx * nx + dy * ny) >= halfA + halfB - OverlapTolerance;
        }
    }
}
