using System.Numerics;

using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Decides whether a bound doodad's saved local transform still matches the offset its house template
/// gives it.
/// </summary>
/// <remarks>
/// Kept free of world state so the rule can be exercised directly. Both position and orientation are
/// compared, because the realignment applies both: comparing position alone leaves a doodad that drifted
/// only in rotation sitting wrong forever, since the check that guards the write never fires.
/// </remarks>
public static class BoundDoodadAlignment
{
    /// <summary>Positional difference, in metres, below which a doodad counts as already aligned.</summary>
    public const float PositionToleranceMetres = 0.01f;

    /// <summary>Angular difference, in radians, below which an axis counts as already aligned.</summary>
    public const float RotationToleranceRadians = 0.0017f; // ~0.1 degrees

    /// <summary>
    /// Whether <paramref name="currentPosition"/> and <paramref name="currentRotationRadians"/> differ
    /// from <paramref name="target"/> by more than the tolerances above.
    /// </summary>
    /// <param name="currentRotationRadians">Local rotation as (roll, pitch, yaw) in radians.</param>
    /// <param name="target">The template offset, whose angles are in degrees.</param>
    public static bool NeedsRealignment(Vector3 currentPosition, Vector3 currentRotationRadians,
        WorldSpawnPosition target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Vector3.Distance(currentPosition, target.AsPositionVector()) >= PositionToleranceMetres)
            return true;

        return ExceedsAngleTolerance(currentRotationRadians.X, target.Roll.DegToRad())
               || ExceedsAngleTolerance(currentRotationRadians.Y, target.Pitch.DegToRad())
               || ExceedsAngleTolerance(currentRotationRadians.Z, target.Yaw.DegToRad());
    }

    /// <summary>
    /// Shortest angular distance between two angles, so that values a whole turn apart - or either side
    /// of the wrap point, such as +179 and -179 degrees - are recognised as the same orientation rather
    /// than as the largest possible difference.
    /// </summary>
    private static bool ExceedsAngleTolerance(float currentRadians, float targetRadians)
    {
        const float fullTurn = 2f * MathF.PI;

        var difference = MathF.IEEERemainder(currentRadians - targetRadians, fullTurn);
        return MathF.Abs(difference) >= RotationToleranceRadians;
    }
}
