namespace AAEmu.Game.Models.Game.Units.Movements;

/// <summary>
/// Dedicate can emit a stand for every unit every tick. Those must not become
/// SCUnitMovements or rewrite World transforms — the client treats a repeat
/// stand as a pose reset (flicker), and a sub-metre rewrite can hop a 64 m
/// region line.
/// </summary>
public static class UnitIdleMoveRules
{
    /// <summary>
    /// Tolerance for treating a zone report as a repeat of the known pose.
    /// Callers fail open: any wider delta is handled as real movement.
    /// </summary>
    public const float SamePositionMetres = 0.15f;

    /// <summary>Heading steps in a full circle; also the modulus for facing compares.</summary>
    public const int HeadingSteps = 128;

    public static bool IsStationary(
        short velX, short velY, short velZ,
        sbyte deltaX, sbyte deltaY, sbyte deltaZ)
    {
        return velX == 0 && velY == 0 && velZ == 0
               && deltaX == 0 && deltaY == 0 && deltaZ == 0;
    }

    public static bool IsSamePosition(
        float knownX, float knownY, float knownZ,
        float moveX, float moveY, float moveZ,
        float epsilonMetres = SamePositionMetres)
    {
        var dx = knownX - moveX;
        var dy = knownY - moveY;
        var dz = knownZ - moveZ;
        var max = epsilonMetres < 0f ? 0f : epsilonMetres;
        return dx * dx + dy * dy + dz * dz <= max * max;
    }

    /// <summary>
    /// Headings are packed into <see cref="HeadingSteps"/> steps around the circle
    /// (<c>MathUtil.ConvertDegreeToSByteDirection</c>), so the distance between two
    /// steps is circular: 85 and -42 are neighbours and must compare equal.
    /// </summary>
    public static bool IsSameFacing(
        sbyte knownX, sbyte knownY, sbyte knownZ,
        sbyte moveX, sbyte moveY, sbyte moveZ,
        int tolerance = 1)
    {
        if (tolerance < 0)
            tolerance = 0;
        return CircularDelta(knownX, moveX) <= tolerance
               && CircularDelta(knownY, moveY) <= tolerance
               && CircularDelta(knownZ, moveZ) <= tolerance;
    }

    /// <summary>
    /// Whether a zone report may be withheld from clients. <paramref name="lastRelayedWasStationary"/>
    /// is what the relay last accepted for this unit: see the stand-after-motion note below.
    /// </summary>
    public static bool ShouldSuppress(
        float knownX, float knownY, float knownZ,
        sbyte knownRx, sbyte knownRy, sbyte knownRz,
        float moveX, float moveY, float moveZ,
        sbyte moveRx, sbyte moveRy, sbyte moveRz,
        short velX, short velY, short velZ,
        sbyte deltaX, sbyte deltaY, sbyte deltaZ,
        bool lastRelayedWasStationary)
    {
        if (!IsStationary(velX, velY, velZ, deltaX, deltaY, deltaZ))
            return false;

        // The stand that ends a walk lands a few centimetres past the last moving record and usually
        // repeats its heading, so the pose test alone calls it a duplicate of it. Withholding that one
        // leaves the client, whose last word for the unit was "moving", to slide the NPC to a halt. A
        // stand may therefore only be withheld while clients already believe the unit is standing.
        if (!lastRelayedWasStationary)
            return false;

        if (!IsSamePosition(knownX, knownY, knownZ, moveX, moveY, moveZ))
            return false;
        return IsSameFacing(knownRx, knownRy, knownRz, moveRx, moveRy, moveRz);
    }

    /// <summary>Shortest heading distance around the 128-step circle.</summary>
    private static int CircularDelta(sbyte a, sbyte b)
    {
        var delta = (a - b) % HeadingSteps;
        if (delta < 0)
            delta += HeadingSteps;
        return delta <= HeadingSteps / 2 ? delta : HeadingSteps - delta;
    }
}
