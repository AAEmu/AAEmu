#nullable enable

using System.Numerics;
using AAEmu.Game.Models.Game.Units;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Tow force when a ship harpoon is hooked to dry land: if the paid rope length is not slack
/// (<see cref="SlackMarginMeters"/>), accelerate the parent hull toward the hook in the horizontal plane.
/// Applied after helm/throttle velocity so <see cref="Slave.Speed"/> is resynced from the clamped along-bow component.
/// </summary>
public static class ShipHarpoonTowPhysics
{
    /// <summary>m/s² per meter of (cannon–hook distance − paid rope length) in the taut regime.</summary>
    public const float TowAccelPerMeterStretch = 4.5f;

    public const float TowMaxAccel = 12f;

    /// <summary>If paid rope exceeds distance by more than this, treat as slack (no tow).</summary>
    public const float SlackMarginMeters = 0.45f;

    /// <summary>Ignore hooks this close to hull center (avoids spikes).</summary>
    public const float MinHookHorizontalDistance = 0.2f;

    public static void ApplyTerrainHookTow(
        Slave hull,
        RigidBody rigidBody,
        float dtSec,
        float bowDirX,
        float bowDirZ,
        float speedToAlongVelScale,
        float maxForwardSpeed,
        float maxBackwardSpeed)
    {
        if (hull.AttachedSlaves.Count == 0 || dtSec <= 0f)
            return;

        var sum = JVector.Zero;
        foreach (var child in hull.AttachedSlaves)
        {
            var st = child.HarpoonRope;
            if (!st.IsEngaged || !st.HookAttachedToTerrain)
                continue;

            var cannonPos = child.Transform.World.Position;
            var hook = st.HookWorld;
            var dist = Vector3.Distance(cannonPos, hook);
            var paid = st.RopeLength;
            if (paid > dist + SlackMarginMeters)
                continue;

            var stretch = MathF.Max(0f, dist - paid);
            var accel = MathF.Min(TowMaxAccel, TowAccelPerMeterStretch * stretch);
            if (accel <= 0f)
                continue;

            var hx = rigidBody.Position.X;
            var hz = rigidBody.Position.Z;
            var dx = hook.X - hx;
            var dz = hook.Y - hz;
            var len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < MinHookHorizontalDistance)
                continue;

            dx /= len;
            dz /= len;
            var imp = accel * dtSec;
            sum.X += dx * imp;
            sum.Z += dz * imp;
        }

        if (sum.LengthSquared() < 1e-10f)
            return;

        var vx = rigidBody.Velocity.X + sum.X;
        var vz = rigidBody.Velocity.Z + sum.Z;
        var alongVel = vx * bowDirX + vz * bowDirZ;
        var mul = speedToAlongVelScale;
        if (mul < 1e-4f)
            mul = 1e-4f;

        var maxAlong = maxForwardSpeed * mul;
        var minAlong = maxBackwardSpeed * mul;
        alongVel = Math.Clamp(alongVel, minAlong, maxAlong);

        var perpX = vx - alongVel * bowDirX;
        var perpZ = vz - alongVel * bowDirZ;

        rigidBody.Velocity = new JVector(
            alongVel * bowDirX + perpX,
            rigidBody.Velocity.Y,
            alongVel * bowDirZ + perpZ);

        hull.Speed = alongVel / mul;
    }
}
