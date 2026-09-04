using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Movements;

/// <summary>
/// Builds the ship body (<see cref="MoveTypeEnum.Ship"/>) that is replayed to a zone which is about
/// to simulate a hull.
/// </summary>
/// <remarks>
/// A zone applies a ship body to its network movement controller only while its own ship simulation
/// is switched off, and enabling the helm hands that pose over to the simulation. A hull that was
/// created but never given a pose still has its physical body at the level origin, so a helm enabled
/// straight after the create simulates from there.
///
    /// The motion on this seed is not a hint the receiver may discard. A type-4 body is accepted
    /// while simulation is still off; enabling the helm flushes that body to the physics entity
    /// once — position, quat, angular velocity and the seeded linear velocity together — so the
    /// seed is what the new body starts from. Reports before that flush has settled (the new
    /// zone's outbound type-4 for an unconsumed or at-rest body) are not a measurement of the hull.
///
/// A rest seed starts the new body at standstill: thrust then rebuilds way from zero, which
/// riders feel as the wall. A carried-momentum seed starts it with the way it crossed with, so
/// nothing is rebuilt. The seam impulse adds velocity on top of whatever the flush left; the
/// closed-loop correction measures the shortfall on the first usable pose instead of predicting
/// what the flush delivered — that fraction is not fixed.
/// </remarks>
public static class ShipPoseSeed
{
    /// <summary>Setting this environment variable to "0" seeds rest instead of the reported motion.</summary>
    public const string EnvCarryMomentum = "AAEMU_SHIP_SEAM_CARRY_MOMENTUM";

    /// <summary>
    /// On, because the flush starts the new body from the seeded velocity (see class remarks) — a
    /// rest seed rebuilds way from zero at every seam; it is not a neutral starting point. Kept as a switch
    /// so a crossing can be measured both ways without a rebuild.
    /// </summary>
    public static bool CarryMomentum =>
        !string.Equals(
            Environment.GetEnvironmentVariable(EnvCarryMomentum), "0", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Current hull pose for the zone that is about to take simulation, carrying the motion the
    /// outgoing simulator last reported.
    /// </summary>
    public static ShipMoveType ForSlave(Slave slave) => ForSlave(slave, CarryMomentum);

    /// <summary>
    /// The hull's model figure scaled by the same move-speed attribute the simulator itself applies.
    /// Diagnostic only — see the remarks before using it as a bound.
    /// </summary>
    /// <remarks>
    /// This is the simulator's <em>thrust cut-off</em>, not a speed limit. Thrust is scaled down as speed
    /// approaches it and stops at it, so a hull settles wherever thrust balances drag — comfortably below
    /// this figure in normal sailing — and can still be carried past it by anything that is not thrust.
    /// Nothing in the simulation clamps velocity to it. Treating it as a maximum therefore both rejects
    /// ordinary sailing as an over-read and throws away real way, which is why neither the seed nor the
    /// seam impulse bounds itself by it any more.
    ///
    /// Also never compare a hull's speed against <c>ship_models.velocity</c> alone: sails carry large
    /// multipliers, so a rigged hull's figure here is well above its bare model number.
    /// </remarks>
    public static float EffectiveMaxVelocity(Slave slave)
    {
        var model = ModelManager.Instance.GetShipModel(slave?.ModelId ?? 0)?.Velocity ?? 0f;
        if (model <= 0f || slave == null)
            return model;

        var mul = slave.MoveSpeedMul;
        return mul > 0f ? model * mul : model;
    }

    /// <param name="carryMomentum">
    /// Result of <see cref="CarryMomentum"/>; split out so tests can pin both behaviours.
    /// </param>
    public static ShipMoveType ForSlave(Slave slave, bool carryMomentum) =>
        ForSlave(slave, carryMomentum, Environment.TickCount64, 0);

    /// <param name="nowMs"><see cref="Environment.TickCount64"/> when the pose will be sent.</param>
    /// <param name="extraAheadMs">
    /// Extra wait after this send before the incoming zone consumes the seed (the Create
    /// physicalize delay). Seam follow switches on the first consumed-body report, not on a
    /// fixed overlap.
    /// </param>
    /// <summary>
    /// Type-4 body from a frozen seam snapshot, already advanced to that snapshot's activation tick.
    /// Create and the seed must both use this so the state is not advanced a second time from a
    /// later outgoing report.
    /// </summary>
    public static ShipMoveType ForHandoff(Slave slave, in BoatSeamHandoffSnapshot snapshot, bool carryMomentum)
    {
        var pose = new ShipMoveType { Type = MoveTypeEnum.Ship };
        pose.UseSlaveBase(slave);
        var (x, y, z, velX, velY, velZ) = BoatSeamHandoffRules.Propagate(snapshot);
        var (rotX, rotY, rotZ) = BoatSeamHandoffRules.PropagateRotation(snapshot);
        pose.X = x;
        pose.Y = y;
        pose.Z = z;
        pose.RotationX = rotX;
        pose.RotationY = rotY;
        pose.RotationZ = rotZ;
        pose.Throttle = BoatSeamPredictRules.LiveThrottle(
            snapshot.Throttle, slave.ThrottleRequest, slave.Throttle);
        pose.Steering = snapshot.Steering;
        pose.Rpm = snapshot.Rpm;
        pose.Stuck = false;
        pose.Time = BoatSeamHandoffRules.AdvancedTime(snapshot);
        if (carryMomentum)
        {
            pose.AngVelX = snapshot.AngVelX;
            pose.AngVelY = snapshot.AngVelY;
            pose.AngVelZ = snapshot.AngVelZ;
            pose.VelX = velX;
            pose.VelY = velY;
            pose.VelZ = velZ;
        }

        return pose;
    }

    /// <summary>
    /// Type-4 the client sees during a seam: the snapshot evaluated up to the activation plant
    /// x(t1), v(t1). The bridge tick is already clamped there, so Time stays on that plant —
    /// do not keep integrating, and do not zero cruise (18 → 0 on a frozen xyz is the hitch).
    /// Zone B is not streamed until its body is at that same plant and at cruise.
    /// </summary>
    public static ShipMoveType ForBridge(Slave slave, in BoatSeamHandoffSnapshot snapshot, long nowMs)
    {
        var pose = new ShipMoveType { Type = MoveTypeEnum.Ship };
        pose.UseSlaveBase(slave);
        var at = BoatSeamHandoffRules.ClientBridgeTick(snapshot, nowMs);
        var (x, y, z, velX, velY, velZ) = BoatSeamHandoffRules.EvaluateAt(snapshot, at);
        var (rotX, rotY, rotZ) = BoatSeamHandoffRules.EvaluateRotation(snapshot, at);
        pose.X = x;
        pose.Y = y;
        pose.Z = z;
        pose.RotationX = rotX;
        pose.RotationY = rotY;
        pose.RotationZ = rotZ;
        pose.Throttle = BoatSeamPredictRules.LiveThrottle(
            snapshot.Throttle, slave.ThrottleRequest, slave.Throttle);
        pose.Steering = slave.SteeringRequest != 0 ? slave.SteeringRequest : snapshot.Steering;
        pose.Rpm = snapshot.Rpm;
        pose.Stuck = false;
        pose.Time = BoatSeamHandoffRules.EvaluateTime(snapshot, at);
        pose.ZoneId = snapshot.ToZone != 0 ? (ushort)snapshot.ToZone : pose.ZoneId;
        pose.AngVelX = snapshot.AngVelX;
        pose.AngVelY = snapshot.AngVelY;
        pose.AngVelZ = snapshot.AngVelZ;
        pose.VelX = velX;
        pose.VelY = velY;
        pose.VelZ = velZ;
        return pose;
    }

    public static ShipMoveType ForSlave(Slave slave, bool carryMomentum, long nowMs, long extraAheadMs)
    {
        if (slave.SeamHandoff is { } handoff && handoff.ToZone != 0)
            return ForHandoff(slave, handoff, carryMomentum);

        return FromLastReport(slave, carryMomentum, nowMs, extraAheadMs);
    }

    /// <summary>
    /// The pose Zone A is streaming right now. Never the seam snapshot — that plant is only
    /// B's Create, and putting it on B again at follow-switch is the one-second stop.
    /// </summary>
    public static ShipMoveType ForLiveReport(Slave slave) =>
        ForLiveReport(slave, CarryMomentum);

    /// <param name="carryMomentum">
    /// Split out so an overlap helm-on can seed the live report's way. Rest plus a full
    /// cruise impulse is the seam speed bump.
    /// </param>
    public static ShipMoveType ForLiveReport(Slave slave, bool carryMomentum) =>
        FromLastReport(slave, carryMomentum, Environment.TickCount64, 0);

    /// <summary>
    /// Upright rest at a waterline Z. Roll and pitch from a tumbled report are discarded; yaw is
    /// kept. Motion is zero so the body does not walk off the plant.
    /// </summary>
    public static ShipMoveType ForWaterlineRecover(Slave slave, float x, float y, float z)
    {
        var pose = new ShipMoveType { Type = MoveTypeEnum.Ship };
        pose.UseSlaveBase(slave);
        pose.X = x;
        pose.Y = y;
        pose.Z = z;
        pose.VelX = 0;
        pose.VelY = 0;
        pose.VelZ = 0;
        pose.AngVelX = 0;
        pose.AngVelY = 0;
        pose.AngVelZ = 0;
        pose.Rpm = 0;
        pose.Stuck = false;

        short yawX = 0, yawY = 0, yawZ = 0;
        if (slave.SimulatedShipState is { } last)
        {
            var (_, _, yaw) = MathUtil.GetSlaveRotationInDegrees(
                last.RotationX, last.RotationY, last.RotationZ);
            (yawX, yawY, yawZ) = MathUtil.GetSlaveRotationFromDegrees(0f, 0f, yaw);
            pose.Throttle = last.Throttle;
            pose.Steering = last.Steering;
            if (last.ZoneId != 0)
                pose.ZoneId = last.ZoneId;
        }
        else
        {
            (yawX, yawY, yawZ) = MathUtil.GetSlaveRotationFromDegrees(
                0f, 0f, slave.Transform.World.Rotation.Z);
        }

        pose.RotationX = yawX;
        pose.RotationY = yawY;
        pose.RotationZ = yawZ;
        return pose;
    }

    private static ShipMoveType FromLastReport(Slave slave, bool carryMomentum, long nowMs, long extraAheadMs)
    {
        var pose = new ShipMoveType { Type = MoveTypeEnum.Ship };
        pose.UseSlaveBase(slave);

        // Prefer the last zone-reported XYZ/facing when Transform has lagged a handoff behind.
        if (slave.SimulatedShipState is { } last)
        {
            var ageMs = slave.SimulatedShipStateAtMs == 0 ? 0 : nowMs - slave.SimulatedShipStateAtMs;

            // A report can claim way the hull is not making (see HullReportedMotionRules). Neither
            // carry nor extrapolate such a figure: both would feed it straight back to the simulator.
            var moving = IsReportedMotionReal(slave, last, nowMs);
            var (x, y, z) = BoatSeamPredictRules.Advance(
                last.X, last.Y, last.Z, last.VelX, last.VelY, last.VelZ,
                moving ? BoatSeamPredictRules.AheadMs(ageMs, extraAheadMs) : 0);
            pose.X = x;
            pose.Y = y;
            pose.Z = z;
            pose.RotationX = last.RotationX;
            pose.RotationY = last.RotationY;
            pose.RotationZ = last.RotationZ;
            pose.Throttle = BoatSeamPredictRules.LiveThrottle(
                last.Throttle, slave.ThrottleRequest, slave.Throttle);
            pose.Steering = last.Steering;
            pose.Rpm = last.Rpm;
            pose.ZoneId = last.ZoneId != 0 ? last.ZoneId : pose.ZoneId;

            if (carryMomentum && moving)
                CarryMotion(pose, last);
        }

        pose.Stuck = false;
        return pose;
    }

    /// <summary>
    /// Whether the hull's travel backs up the velocity its last report carried. Rest is seeded when
    /// it does not, which is also what stops the leftover from being echoed back and forth.
    /// </summary>
    public static bool IsReportedMotionReal(Slave slave, ShipMoveType last, long nowMs) =>
        HullReportedMotionRules.IsReportedMotionCorroborated(
            last.ReportedSpeed,
            slave.SimulatedSpeed,
            slave.SimulatedSpeedAtMs == 0
                ? HullReportedMotionRules.FreshnessWindowMs
                : nowMs - slave.SimulatedSpeedAtMs);

    /// <summary>
    /// Copies the motion the outgoing simulator last reported onto the seed, verbatim.
    /// </summary>
    /// <remarks>
    /// Nothing is scaled or bounded here. These fields already are the hull's own reported velocity, so
    /// there is no figure to correct towards; the only ceiling that applies is the one the quantised
    /// fields impose by construction. In particular this must not be bounded by
    /// <see cref="EffectiveMaxVelocity"/>, which is a thrust cut-off rather than a speed limit — a hull
    /// legitimately sails below it and can transiently exceed it, so clamping to it discards real way
    /// and asks the receiving zone to hold the hull slower than it arrived.
    /// </remarks>
    private static void CarryMotion(ShipMoveType pose, ShipMoveType last)
    {
        pose.AngVelX = last.AngVelX;
        pose.AngVelY = last.AngVelY;
        pose.AngVelZ = last.AngVelZ;
        pose.VelX = last.VelX;
        pose.VelY = last.VelY;
        pose.VelZ = last.VelZ;
    }

    /// <summary>Movement payload as a zone expects it: the move type byte followed by the body.</summary>
    public static byte[] Build(ShipMoveType pose)
    {
        var stream = new PacketStream();
        stream.Write((byte)MoveTypeEnum.Ship);
        pose.Write(stream);
        return stream.GetBytes();
    }
}
