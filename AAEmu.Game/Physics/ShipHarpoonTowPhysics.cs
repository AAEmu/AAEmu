#nullable enable

using System.Numerics;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Debug;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Tow force when a ship harpoon is hooked to dry land: if the paid rope length is not slack
/// (<see cref="SlackMarginMeters"/>), accelerate the parent hull toward the hook in the horizontal plane.
/// Optional bow yaw toward average tow pull (<see cref="TowYawAssistRadPerSec"/>). Applied after helm/throttle velocity so <see cref="Slave.Speed"/> is resynced from the clamped along-bow component.
/// Tunables are not <c>const</c> — edit the private <c>Get*()</c> return literals below and save; <c>dotnet watch</c> hot reload applies like <see cref="ShipTuningDebug"/>.
/// </summary>
public static class ShipHarpoonTowPhysics
{
    #region Hot-reload tunables (edit literal in Get* body, save — dotnet watch)

    /// <summary>m/s² per meter of (cannon–hook distance − paid rope length) in the taut regime.</summary>
    public static float TowAccelPerMeterStretch => GetTowAccelPerMeterStretch();
    private static float GetTowAccelPerMeterStretch() => 1f;

    /// <summary>Cap on tow acceleration toward the hook (m/s²).</summary>
    public static float TowMaxAccel => GetTowMaxAccel();
    private static float GetTowMaxAccel() => 1f;

    /// <summary>If paid rope exceeds distance by more than this, treat as slack (no tow).</summary>
    public static float SlackMarginMeters => GetSlackMarginMeters();
    private static float GetSlackMarginMeters() => 0.5f;

    /// <summary>Ignore hooks this close to hull center (avoids spikes).</summary>
    public static float MinHookHorizontalDistance => GetMinHookHorizontalDistance();
    private static float GetMinHookHorizontalDistance() => 0.2f;

    /// <summary>
    /// Hack: added to <c>RopeLength</c> (client / initial stored value) only when judging slack/stretch in tow — avoids double-count at launch.
    /// Tune if fixed payout / <c>len</c> disagrees with server chord; hot-reload.
    /// </summary>
    public static float ServerRopePaidLengthAdditiveMeters => GetServerRopePaidLengthAdditiveMeters();
    private static float GetServerRopePaidLengthAdditiveMeters() => 12.5f;

    /// <summary>Yaw rate assist (rad/s per unit cross) so bow follows tow pull; use <c>-cross</c> vs body angular velocity sign.</summary>
    public static float TowYawAssistRadPerSec => GetTowYawAssistRadPerSec();
    private static float GetTowYawAssistRadPerSec() => 0.3f;

    #endregion

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
            var hook = ShipHarpoonRopeController.GetHookWorldPosition(child);
            var dist = Vector3.Distance(cannonPos, hook);
            var paid = st.RopeLength + ServerRopePaidLengthAdditiveMeters;
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

        var sumLen = MathF.Sqrt(sum.X * sum.X + sum.Z * sum.Z);
        if (sumLen < 1e-6f)
            return;
        var sx = sum.X / sumLen;
        var sz = sum.Z / sumLen;
        var cross = bowDirX * sz - bowDirZ * sx;
        // ShipController sets angular velocity as -RotSpeed on rigid body Y — negate cross so bow turns toward pull.
        hull.RotSpeed += Math.Clamp(-cross * TowYawAssistRadPerSec * dtSec, -0.85f, 0.85f);
    }
}
