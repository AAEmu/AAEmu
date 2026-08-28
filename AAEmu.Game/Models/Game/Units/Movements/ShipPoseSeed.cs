using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;

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
    public static ShipMoveType ForSlave(Slave slave, bool carryMomentum)
    {
        var pose = new ShipMoveType { Type = MoveTypeEnum.Ship };
        pose.UseSlaveBase(slave);

        // Prefer the last zone-reported XYZ/facing when Transform has lagged a handoff behind.
        if (slave.SimulatedShipState is { } last)
        {
            pose.X = last.X;
            pose.Y = last.Y;
            pose.Z = last.Z;
            pose.RotationX = last.RotationX;
            pose.RotationY = last.RotationY;
            pose.RotationZ = last.RotationZ;
            pose.Throttle = PreferLiveThrottle(slave, last.Throttle);
            pose.Steering = last.Steering;
            pose.Rpm = last.Rpm;
            pose.ZoneId = last.ZoneId != 0 ? last.ZoneId : pose.ZoneId;

            if (carryMomentum)
                CarryMotion(pose, last);
        }

        pose.Stuck = false;
        return pose;
    }

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
    /// <summary>
    /// The last type-4 body sometimes reports throttle 0 for a single frame at a seam while the
    /// rider is still holding the wheel. Seeding that 0 puts the new simulator in its braking
    /// branch. Prefer the live helm request, then the smoothed helm World already applied.
    /// </summary>
    private static sbyte PreferLiveThrottle(Slave slave, sbyte reported)
    {
        if (reported != 0)
            return reported;
        if (slave.ThrottleRequest != 0)
            return slave.ThrottleRequest;
        return slave.Throttle;
    }

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
