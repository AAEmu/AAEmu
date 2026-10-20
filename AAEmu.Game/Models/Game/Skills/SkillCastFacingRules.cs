using System.Numerics;

using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Cast-time aim. Area stands, controller runs and <c>SCPlotEvent</c> POSITION poses read the caster's
/// Transform yaw. A ground-aimed cast plants the yaw toward its aim point; a <c>use_input_direction</c>
/// skill plants the look byte <c>CSStartSkill.inputDirection</c> carries (same sbyte encoding as movement
/// yaw). Every other skill keeps the body yaw the client last moved with — the client only fills that
/// byte for the flagged skills, so reading it elsewhere plants a world-fixed heading.
/// </summary>
public static class SkillCastFacingRules
{
    private const float MinAimDistanceSq = 1e-4f;

    /// <summary>
    /// Yaw (radians) that makes <see cref="PositionAndRotation.AddDistanceToFront"/> travel from
    /// <paramref name="from"/> toward <paramref name="to"/> on the XY plane.
    /// </summary>
    public static bool TryGetYawToward(Vector3 from, Vector3 to, out float yawRadians)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx * dx + dy * dy < MinAimDistanceSq)
        {
            yawRadians = 0f;
            return false;
        }

        // AddDistanceToFront(0) = +Y; relative cone math uses atan2(dy,dx)−90° − yawDeg = 0 when ahead.
        yawRadians = MathF.Atan2(dy, dx) - MathF.PI / 2f;
        return true;
    }

    /// <summary>Look byte in movement encoding → Transform yaw, the same conversion a move packet gets.</summary>
    public static float YawFromInputDirection(byte inputDirection) =>
        (float)MathUtil.ConvertDirectionToRadian(unchecked((sbyte)inputDirection));

    public static byte InputDirectionFromYaw(float yawRadians) =>
        unchecked((byte)MathUtil.ConvertRadianToDirectionRounded(yawRadians));

    /// <summary>
    /// Resolves the yaw a cast should plant on the caster before plots/FX run.
    /// Ground aim points (position casts) win. <paramref name="useInputDirection"/> is the skill's
    /// <c>use_input_direction</c>. Unit tab-targets are not an aim source.
    /// </summary>
    public static bool TryResolveCastYaw(
        Vector3 casterPos,
        Vector3? aimWorldPos,
        byte inputDirection,
        bool useInputDirection,
        out float yawRadians)
    {
        if (aimWorldPos is { } aim && TryGetYawToward(casterPos, aim, out yawRadians))
            return true;

        if (useInputDirection)
        {
            yawRadians = YawFromInputDirection(inputDirection);
            return true;
        }

        yawRadians = 0f;
        return false;
    }

    /// <summary>
    /// Whether a cast-target kind may supply an aim point for <see cref="TryResolveCastYaw"/>.
    /// Unit targets are excluded: tab-select must not redirect frontal plots off the body facing.
    /// </summary>
    public static bool AimFromUnitTarget => false;

    /// <summary>
    /// Converts a world-space cast yaw into the parent-local yaw <c>Transform.Local</c> stores.
    /// Unparented casters pass identity (local == world). A ship/house parent composes its own
    /// rotation onto Local, so writing a world heading there aims the cast the parent's yaw off.
    /// </summary>
    public static float LocalYawFromWorldYaw(Quaternion parentWorldRotation, float worldYawRadians)
    {
        var desiredWorld = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, worldYawRadians));
        var local = Quaternion.Normalize(Quaternion.Inverse(parentWorldRotation) * desiredWorld);
        return PositionAndRotation.FromQuaternion(local).Z;
    }
}
