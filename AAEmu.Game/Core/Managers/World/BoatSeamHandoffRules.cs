using System.Numerics;

using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Time-aligns a seam handoff from a server-owned monotonic clock: stop Zone A at transfer,
/// measure Δt, then plant Zone B at the last A snapshot advanced once to that tick.
/// </summary>
/// <remarks>
/// Minimum correction is linear: x₁ = x₀ + v₀Δt, and the movement timestamp advances by the
/// same Δt. Orientation, linear and angular velocity, steering, throttle and RPM are preserved
/// unless a measured acceleration or turn rate is available — then the same interval is
/// fast-forwarded once (x += ½aΔt², v += aΔt, heading += ωΔt). Client coordinates are never
/// part of the snapshot. Δt is capped so an abnormal delay cannot throw the hull into the
/// next sea. A projection that leaves both the source and destination zones is rejected.
/// </remarks>
public static class BoatSeamHandoffRules
{
    /// <summary>
    /// Pair of reports closer than this is quantisation noise, not a measured acceleration.
    /// </summary>
    public const long MinAccelSampleMs = 100;

    /// <summary>
    /// Pair of reports further apart than this is a stall or a prior handoff, not one stretch.
    /// </summary>
    public const long MaxAccelSampleMs = 3000;

    /// <summary>
    /// Glitch gate on the derived acceleration. A real hull does not jump this hard between two
    /// samples; treating that as thrust would launch the plant.
    /// </summary>
    public const float MaxCredibleAccelMetresPerSecondSquared = 10f;

    /// <summary>
    /// Glitch gate on type-4 angular velocity (radians per second). A real hull does not
    /// spin this hard; treating that as ω would flip the plant.
    /// </summary>
    public const float MaxCredibleAngVelRadiansPerSecond = 8f;

    public static bool TryCapture(
        ShipMoveType last,
        long lastAtMs,
        ShipMoveType previous,
        long previousAtMs,
        uint epoch,
        uint fromZone,
        uint toZone,
        long nowMs,
        long extraAheadMs,
        sbyte liveThrottle,
        out BoatSeamHandoffSnapshot snapshot)
    {
        snapshot = default;
        if (last == null || toZone == 0)
            return false;

        var transferTickMs = lastAtMs != 0 ? lastAtMs : nowMs;
        extraAheadMs = Math.Max(0, extraAheadMs);
        var activationTickMs = nowMs + extraAheadMs;
        if (activationTickMs < transferTickMs)
            activationTickMs = transferTickMs;

        var (ax, ay, az) = AccelMetresPerSecondSquared(previous, previousAtMs, last, transferTickMs);

        snapshot = new BoatSeamHandoffSnapshot
        {
            Epoch = epoch,
            Sequence = 1,
            FromZone = fromZone,
            ToZone = toZone,
            TransferTickMs = transferTickMs,
            ActivationTickMs = activationTickMs,
            Time = last.Time,
            X = last.X,
            Y = last.Y,
            Z = last.Z,
            RotationX = last.RotationX,
            RotationY = last.RotationY,
            RotationZ = last.RotationZ,
            VelX = last.VelX,
            VelY = last.VelY,
            VelZ = last.VelZ,
            AccelX = ax,
            AccelY = ay,
            AccelZ = az,
            AngVelX = last.AngVelX,
            AngVelY = last.AngVelY,
            AngVelZ = last.AngVelZ,
            Throttle = liveThrottle,
            Steering = last.Steering,
            Rpm = last.Rpm
        };
        return true;
    }

    /// <summary>
    /// How late the snapshot is when the incoming zone should own the body, in milliseconds.
    /// Capped so a stalled report is not extrapolated into the next sea.
    /// </summary>
    public static long DeltaMs(in BoatSeamHandoffSnapshot snapshot)
    {
        var delta = snapshot.ActivationTickMs - snapshot.TransferTickMs;
        if (delta <= 0)
            return 0;
        return delta > BoatSeamPredictRules.MaxPredictAgeMs ? BoatSeamPredictRules.MaxPredictAgeMs : delta;
    }

    /// <summary>
    /// Position and velocity at the activation tick: x₀ + v₀Δt + ½a₀Δt² and v₀ + a₀Δt.
    /// </summary>
    public static (
        float X, float Y, float Z,
        short VelX, short VelY, short VelZ) Propagate(in BoatSeamHandoffSnapshot snapshot)
    {
        var dt = DeltaMs(snapshot) / 1000f;
        if (dt <= 0f)
        {
            return (snapshot.X, snapshot.Y, snapshot.Z, snapshot.VelX, snapshot.VelY, snapshot.VelZ);
        }

        var vx = BoatSeamPredictRules.DecodeVelMetresPerSecond(snapshot.VelX);
        var vy = BoatSeamPredictRules.DecodeVelMetresPerSecond(snapshot.VelY);
        var vz = BoatSeamPredictRules.DecodeVelMetresPerSecond(snapshot.VelZ);
        var ax = snapshot.AccelX;
        var ay = snapshot.AccelY;
        var az = snapshot.AccelZ;
        return (
            snapshot.X + vx * dt + 0.5f * ax * dt * dt,
            snapshot.Y + vy * dt + 0.5f * ay * dt * dt,
            snapshot.Z + vz * dt + 0.5f * az * dt * dt,
            EncodeVelMetresPerSecond(vx + ax * dt),
            EncodeVelMetresPerSecond(vy + ay * dt),
            EncodeVelMetresPerSecond(vz + az * dt));
    }

    /// <summary>
    /// Heading at the activation tick: the frozen rotation advanced by ω₀Δt. Shorts are the
    /// same quaternion xyz the type-4 body already carries.
    /// </summary>
    public static (short RotationX, short RotationY, short RotationZ) PropagateRotation(
        in BoatSeamHandoffSnapshot snapshot) =>
        EvaluateRotation(snapshot, snapshot.ActivationTickMs);

    public static (short RotationX, short RotationY, short RotationZ) EvaluateRotation(
        in BoatSeamHandoffSnapshot snapshot, long atMs)
    {
        var at = atMs < snapshot.TransferTickMs ? snapshot.TransferTickMs : atMs;
        var dt = (at - snapshot.TransferTickMs) / 1000f;
        if (dt <= 0f)
            return (snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ);

        var wx = snapshot.AngVelX;
        var wy = snapshot.AngVelY;
        var wz = snapshot.AngVelZ;
        if (!IsCredibleAngVel(wx) || !IsCredibleAngVel(wy) || !IsCredibleAngVel(wz))
            return (snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ);

        var speed = MathF.Sqrt(wx * wx + wy * wy + wz * wz);
        if (speed < 1e-4f)
            return (snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ);

        var q = QuatFromRotationShorts(snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ);
        var dq = Quaternion.CreateFromAxisAngle(new Vector3(wx / speed, wy / speed, wz / speed), speed * dt);
        var next = Quaternion.Normalize(q * dq);
        return PositionAndRotation.ToRollPitchYawShorts(next);
    }

    /// <summary>Type-4 <c>Time</c> at the activation tick: transfer stamp plus the same Δt.</summary>
    public static uint AdvancedTime(in BoatSeamHandoffSnapshot snapshot) =>
        AddTime(snapshot.Time, DeltaMs(snapshot));

    /// <summary>Type-4 <c>Time</c> of the snapshot evaluated at <paramref name="atMs"/>.</summary>
    public static uint EvaluateTime(in BoatSeamHandoffSnapshot snapshot, long atMs)
    {
        var at = atMs < snapshot.TransferTickMs ? snapshot.TransferTickMs : atMs;
        var dt = at - snapshot.TransferTickMs;
        if (dt > BoatSeamPredictRules.MaxPredictAgeMs)
            dt = BoatSeamPredictRules.MaxPredictAgeMs;
        return AddTime(snapshot.Time, dt);
    }

    /// <summary>
    /// Rebinds activation to the measured handoff duration, then shrinks Δt if that projection
    /// would leave both the source and destination zones.
    /// </summary>
    public static bool TryBindActivationInDestinationZone(
        in BoatSeamHandoffSnapshot snapshot,
        long activationTickMs,
        Func<float, float, uint> zoneAtXy,
        out BoatSeamHandoffSnapshot bound)
    {
        var at = activationTickMs < snapshot.TransferTickMs
            ? snapshot.TransferTickMs
            : activationTickMs;
        var candidate = snapshot with { ActivationTickMs = at, Sequence = snapshot.Sequence + 1 };
        if (zoneAtXy == null || IsSafeProjection(candidate, zoneAtXy))
        {
            bound = candidate;
            return true;
        }

        var lo = snapshot.TransferTickMs;
        var hi = at;
        var atSource = snapshot with { ActivationTickMs = lo };
        if (!IsSafeProjection(atSource, zoneAtXy))
        {
            bound = atSource with { Sequence = snapshot.Sequence + 1 };
            return false;
        }

        while (hi - lo > 8)
        {
            var mid = lo + (hi - lo) / 2;
            var trial = snapshot with { ActivationTickMs = mid };
            if (IsSafeProjection(trial, zoneAtXy))
                lo = mid;
            else
                hi = mid;
        }

        bound = snapshot with { ActivationTickMs = lo, Sequence = snapshot.Sequence + 1 };
        return true;
    }

    public static bool IsSafeProjection(
        in BoatSeamHandoffSnapshot snapshot,
        Func<float, float, uint> zoneAtXy)
    {
        var (x, y, _, _, _, _) = Propagate(snapshot);
        return IsSafeProjectionZone(zoneAtXy(x, y), snapshot.FromZone, snapshot.ToZone);
    }

    /// <summary>
    /// A projection may sit in the destination or still in the source (the last A report is often
    /// on the A side of the seam). A third zone or an unmapped point is not a safe plant.
    /// </summary>
    public static bool IsSafeProjectionZone(uint projectedZone, uint fromZone, uint toZone) =>
        projectedZone != 0 && (projectedZone == toZone || projectedZone == fromZone);

    public static float LinearSpeed(in BoatSeamHandoffSnapshot snapshot)
    {
        var vx = BoatSeamPredictRules.DecodeVelMetresPerSecond(snapshot.VelX);
        var vy = BoatSeamPredictRules.DecodeVelMetresPerSecond(snapshot.VelY);
        var vz = BoatSeamPredictRules.DecodeVelMetresPerSecond(snapshot.VelZ);
        return MathF.Sqrt(vx * vx + vy * vy + vz * vz);
    }

    public static bool IsClientBridge(in BoatSeamHandoffSnapshot snapshot) => snapshot.ToZone != 0;

    public static bool IsForActivation(in BoatSeamHandoffSnapshot snapshot, uint zoneKey, uint epoch) =>
        snapshot.ToZone != 0 && snapshot.ToZone == zoneKey && snapshot.Epoch == epoch;

    /// <summary>
    /// Create already advanced once to <see cref="BoatSeamHandoffSnapshot.ActivationTickMs"/>.
    /// Arm must reuse that tick. A later wall-clock is not a second Δt.
    /// </summary>
    public static long PlannedActivationTick(in BoatSeamHandoffSnapshot snapshot, long nowMs) =>
        snapshot.ActivationTickMs > 0 ? snapshot.ActivationTickMs : nowMs;

    /// <summary>
    /// Sets the activation tick without touching the frozen transfer state. Create may plant
    /// against a predicted arm; the type-4 seed rebinds to the real arm so the one advance
    /// lands on the tick Zone B actually takes the body.
    /// </summary>
    public static BoatSeamHandoffSnapshot WithActivationTick(
        in BoatSeamHandoffSnapshot snapshot, long activationTickMs)
    {
        if (activationTickMs < snapshot.TransferTickMs)
            activationTickMs = snapshot.TransferTickMs;
        return snapshot with
        {
            ActivationTickMs = activationTickMs,
            Sequence = snapshot.Sequence + 1
        };
    }

    /// <summary>
    /// How far behind the bridged plant a cruise-speed body may still sit and be followed.
    /// Type-4 XY is finer than this; the live rewind was 1–2 m.
    /// </summary>
    public const float CatchUpMetres = 0.5f;

    /// <summary>
    /// Signed metres the body is past the bridged plant along the snapshot velocity.
    /// Negative means the body is still short of the pose the client is already looking at.
    /// </summary>
    public static float AlongTrackMetres(
        float bodyX, float bodyY, float bridgeX, float bridgeY, short velX, short velY)
    {
        var vx = BoatSeamPredictRules.DecodeVelMetresPerSecond(velX);
        var vy = BoatSeamPredictRules.DecodeVelMetresPerSecond(velY);
        var speed = MathF.Sqrt(vx * vx + vy * vy);
        if (speed < BoatSeamImpulse.MinCruiseSpeed)
            return -MathF.Sqrt((bodyX - bridgeX) * (bodyX - bridgeX) + (bodyY - bridgeY) * (bodyY - bridgeY));

        return ((bodyX - bridgeX) * vx + (bodyY - bridgeY) * vy) / speed;
    }

    /// <summary>
    /// True when Zone B's body is at or past the pose the client bridge is streaming. Cruise
    /// alone is not enough: B is created at the transfer xyz while the bridge freezes at the
    /// activation plant (~2 m ahead). Following then rewinds the hull.
    /// </summary>
    public static bool HasReachedClientBridge(
        float bodyX, float bodyY, float bridgeX, float bridgeY, short velX, short velY) =>
        AlongTrackMetres(bodyX, bodyY, bridgeX, bridgeY, velX, velY) >= -CatchUpMetres;

    /// <summary>
    /// <see cref="HasReachedClientBridge(float,float,float,float,short,short)"/> against the
    /// pose the client is actually being shown right now.
    /// </summary>
    public static bool HasReachedClientBridge(
        in BoatSeamHandoffSnapshot snapshot, float bodyX, float bodyY, long nowMs)
    {
        var at = ClientBridgeTick(snapshot, nowMs);
        var (bridgeX, bridgeY, _, _, _, _) = EvaluateAt(snapshot, at);
        return HasReachedClientBridge(bodyX, bodyY, bridgeX, bridgeY, snapshot.VelX, snapshot.VelY);
    }

    /// <summary>
    /// Tick the client bridge may evaluate. The plant is x(t1) as soon as
    /// <see cref="BoatSeamHandoffSnapshot.ActivationTickMs"/> is known — not only after the
    /// Sequence &gt; 1 bind. Predicting past that tick during the Create-to-arm wait, then
    /// clamping on bind, is a one-metre rewind (arm slack × cruise).
    /// </summary>
    public static long ClientBridgeTick(in BoatSeamHandoffSnapshot snapshot, long nowMs)
    {
        var at = nowMs < snapshot.TransferTickMs ? snapshot.TransferTickMs : nowMs;
        if (snapshot.ActivationTickMs > 0 && at > snapshot.ActivationTickMs)
            return snapshot.ActivationTickMs;
        var age = at - snapshot.TransferTickMs;
        if (age > BoatSeamPredictRules.MaxPredictAgeMs)
            return snapshot.TransferTickMs + BoatSeamPredictRules.MaxPredictAgeMs;
        return at;
    }

    /// <summary>
    /// Client-side prediction of the frozen snapshot at an arbitrary tick. Does not change what
    /// Zone B was planted at — that advance happens once, via <see cref="WithActivationTick"/>.
    /// </summary>
    public static (
        float X, float Y, float Z,
        short VelX, short VelY, short VelZ) EvaluateAt(in BoatSeamHandoffSnapshot snapshot, long atMs)
    {
        var at = atMs < snapshot.TransferTickMs ? snapshot.TransferTickMs : atMs;
        return Propagate(snapshot with { ActivationTickMs = at });
    }

    public static (float X, float Y, float Z) AccelMetresPerSecondSquared(
        ShipMoveType previous,
        long previousAtMs,
        ShipMoveType last,
        long lastAtMs)
    {
        if (previous == null || last == null || previousAtMs == 0 || lastAtMs == 0)
            return (0f, 0f, 0f);

        var elapsed = lastAtMs - previousAtMs;
        if (elapsed < MinAccelSampleMs || elapsed > MaxAccelSampleMs)
            return (0f, 0f, 0f);

        var dt = elapsed / 1000f;
        var ax = (BoatSeamPredictRules.DecodeVelMetresPerSecond(last.VelX)
                  - BoatSeamPredictRules.DecodeVelMetresPerSecond(previous.VelX)) / dt;
        var ay = (BoatSeamPredictRules.DecodeVelMetresPerSecond(last.VelY)
                  - BoatSeamPredictRules.DecodeVelMetresPerSecond(previous.VelY)) / dt;
        var az = (BoatSeamPredictRules.DecodeVelMetresPerSecond(last.VelZ)
                  - BoatSeamPredictRules.DecodeVelMetresPerSecond(previous.VelZ)) / dt;
        if (!IsCredibleAccel(ax) || !IsCredibleAccel(ay) || !IsCredibleAccel(az))
            return (0f, 0f, 0f);

        return (ax, ay, az);
    }

    public static short EncodeVelMetresPerSecond(float metresPerSecond)
    {
        var quantised = metresPerSecond / ShipMoveType.VelocityQuantizationScale * short.MaxValue;
        if (quantised > short.MaxValue)
            return short.MaxValue;
        if (quantised < short.MinValue)
            return short.MinValue;
        return (short)MathF.Round(quantised);
    }

    private static uint AddTime(uint time, long deltaMs)
    {
        if (deltaMs <= 0)
            return time;
        var next = time + (ulong)deltaMs;
        return next > uint.MaxValue ? uint.MaxValue : (uint)next;
    }

    private static bool IsCredibleAccel(float metresPerSecondSquared) =>
        MathF.Abs(metresPerSecondSquared) <= MaxCredibleAccelMetresPerSecondSquared + 0.25f;

    private static bool IsCredibleAngVel(float radiansPerSecond) =>
        MathF.Abs(radiansPerSecond) <= MaxCredibleAngVelRadiansPerSecond;

    /// <summary>
    /// Inverse of <see cref="PositionAndRotation.ToRollPitchYawShorts(Quaternion)"/>.
    /// </summary>
    public static Quaternion QuatFromRotationShorts(short rotX, short rotY, short rotZ)
    {
        var x = rotX / (float)short.MaxValue;
        var y = rotY / (float)short.MaxValue;
        var z = rotZ / (float)short.MaxValue;
        var norm = x * x + y * y + z * z;
        var w = norm < 0.99750f ? MathF.Sqrt(1f - norm) : 0f;
        return Quaternion.Normalize(new Quaternion(x, y, z, w));
    }
}

/// <summary>One helm stick sample held while a seam is in flight.</summary>
public readonly record struct BoatSeamHelmSample(sbyte Throttle, sbyte Steering);
