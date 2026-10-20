using System.Numerics;

using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Where a controller run that is anchored on the owner itself goes. The unit's front is what
/// <see cref="PositionAndRotation.AddDistanceToFront"/> walks (yaw 0 = +Y); the <c>(cos, sin)</c> helpers
/// in <c>MathUtil</c> take a heading measured from +X, so the facing is handed to them as yaw + 90°.
/// </summary>
public static class LinearMoveFacingRules
{
    private const float CoincidentSq = 1e-4f;

    /// <summary>
    /// Heading (radians, from +X) for a run of <paramref name="signedDistance"/> along the owner's facing:
    /// positive runs forward, negative runs backward (Backdraft, the 8 m / 2 s dash family).
    /// </summary>
    public static float HeadingAlongFacing(float yawRadians, float signedDistance) =>
        yawRadians + MathF.PI / 2f + (signedDistance < 0f ? MathF.PI : 0f);

    /// <summary>
    /// A leap whose target is the owner (self-cast rows: Backdraft −5 m, Tactical Move +6 m) or stands on
    /// it has no owner→target direction; it runs along the facing instead.
    /// </summary>
    public static bool LeapRunsAlongFacing(uint ownerObjId, uint targetObjId, Vector3 ownerPos, Vector3 targetPos)
    {
        if (ownerObjId == targetObjId)
            return true;
        var dx = targetPos.X - ownerPos.X;
        var dy = targetPos.Y - ownerPos.Y;
        return dx * dx + dy * dy < CoincidentSq;
    }

    /// <summary>
    /// A run that goes backward keeps the owner facing where it was — Backdraft fires at the target while
    /// stepping away from it. Forward runs turn into their travel direction as before.
    /// </summary>
    public static bool TurnsIntoTravel(float signedDistance) => signedDistance >= 0f;
}
