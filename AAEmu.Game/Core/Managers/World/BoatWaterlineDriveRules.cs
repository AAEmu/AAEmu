using System.Numerics;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Helm step for a no-tube hull whose zone simulation stays off.
/// </summary>
/// <remarks>
/// Growling and other tube hulls stay on zone physics (type-5 thrust and rudder). Ostera's
/// <c>ship_models</c> tube is 0/0, so simulation stays off and World steps the helm here.
/// Type-5 is ignored while simulation is off; type-4 is accepted.
/// Speed is <c>ship_models.velocity</c> (or <c>reverse_velocity</c>) at full throw — not the
/// sail-scaled thrust cut-off. Yaw rate is <c>ship_models.steer_vel</c>. Positive steer
/// decreases yaw so A/D matches the zone rudder (bow right from heading +Y). Forward is
/// <c>(-sin yaw, cos yaw)</c>. Z is the water surface. Rotation shorts use the same
/// Transform quaternion as <c>ShipMoveType.UseSlaveBase</c>.
/// </remarks>
public static class BoatWaterlineDriveRules
{
    /// <summary>
    /// Dedicate publishes a type-4 every 50 ms. First helm packet after occupy uses that
    /// so a zero clock does not skip the step.
    /// </summary>
    public const float DefaultStepSeconds = 0.05f;

    /// <summary>
    /// A couple of missed helm packets, not the seam freshness window. Integrating over
    /// seconds at cruise speed teleports the hull.
    /// </summary>
    public const float MaxStepSeconds = DefaultStepSeconds * 4f;

    /// <summary>
    /// Type-5 +127 is starboard. Transform yaw 0 faces +Y; a right turn is negative yaw.
    /// The streamed steering byte stays the stick (rudder mesh).
    /// </summary>
    public const float SteerYawSign = -1f;

    public static float ClampStepSeconds(float dt)
    {
        if (dt <= 0f)
            return DefaultStepSeconds;
        return dt > MaxStepSeconds ? MaxStepSeconds : dt;
    }

    public static float Stick(sbyte value) => value / 127f;

    /// <summary>
    /// Bare model cruise. Reverse uses <c>reverse_velocity</c> when that column is set.
    /// </summary>
    public static float CruiseSpeed(sbyte throttle, float velocity, float reverseVelocity)
    {
        if (throttle < 0 && reverseVelocity > 0f)
            return reverseVelocity;
        return velocity;
    }

    /// <summary>
    /// Same shorts <see cref="ShipMoveType.UseSlaveBase"/> writes from Transform.
    /// </summary>
    public static (short X, short Y, short Z) RotationShortsFromYaw(float yaw)
    {
        var q = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, yaw));
        return PositionAndRotation.ToRollPitchYawShorts(q);
    }

    /// <summary>
    /// Continent step. <paramref name="yaw"/> is radians (Transform.World.Rotation.Z).
    /// </summary>
    public static (float X, float Y, float Z, float Yaw, float VelX, float VelY) Step(
        float x,
        float y,
        float surfaceZ,
        float yaw,
        sbyte throttle,
        sbyte steering,
        float maxVelocity,
        float steerVel,
        float dt)
    {
        dt = ClampStepSeconds(dt);
        var nextYaw = yaw + SteerYawSign * Stick(steering) * steerVel * dt;
        var speed = Stick(throttle) * maxVelocity;
        var velX = -MathF.Sin(nextYaw) * speed;
        var velY = MathF.Cos(nextYaw) * speed;
        return (x + velX * dt, y + velY * dt, surfaceZ, nextYaw, velX, velY);
    }
}
