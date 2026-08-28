namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Re-drives a hull through a zone seam instead of letting it restart from rest.
/// </summary>
/// <remarks>
    /// The skill impulse is the channel that can put velocity on a controlled hull immediately.
    /// Authored drive rows put their magnitude on +Y, which the self-cast rotation lands on the bow.
    /// It is the closed-loop half of a seam: the arm-time flush hands the crossing's way to the new
    /// body once, and this restore is the measured shortfall once a usable pose exists — the flush
    /// transient and drag eat an unpredictable fraction of the seed, so the gap is measured, not
    /// predicted.
///
/// Speed is not guessed: World already measures what the outgoing simulator achieves from the
/// positions it reports (see <see cref="World.Core.Relay.HullSpeedMonitor"/>), and that figure is
/// captured here. A restore happens only while the hull was demonstrably under way recently and
/// the helm still holds way; anything else keeps the rest seed.
/// </remarks>
public static class BoatSeamImpulse
{
    /// <summary>Setting this environment variable to "0" disables the restore.</summary>
    public const string EnvKillSwitch = "AAEMU_SHIP_SEAM_IMPULSE";

    /// <summary>A hull slower than this is barely moving; the thrust curve recovers it instantly.</summary>
    public const float MinCruiseSpeed = 2f;

    /// <summary>Speed measurements older than this describe a hull that has since stopped or docked.</summary>
    public const long FreshnessWindowMs = 5000;

    /// <summary>
    /// Fraction of the measured speed restored. The measurement is the hull's speed made good over the
    /// ground, so continuing it in full is what leaves the crossing unfelt; anything less is a dip the
    /// riders notice at every border. It used to hold back a tenth to absorb an over-read from vertical
    /// wave motion, which is now excluded from the measurement instead.
    /// </summary>
    public const float RestoreFactor = 1.0f;

    /// <summary>Sanity ceiling on the restored speed regardless of what was measured.</summary>
    public const float MaxRestoredSpeed = 30f;

    /// <summary>
    /// Shortfall below which a seam is left alone. Chosen to sit far above what the velocity fields can
    /// even express — they are 16-bit over <see cref="ShipMoveType.VelocityQuantizationScale"/> m/s, so
    /// about a thousandth of a metre per second — while staying far below the several m/s losses a
    /// handover actually produces. Inside this band the thrust curve closes the gap within the same
    /// sampling window, so correcting would chase noise.
    /// </summary>
    public const float MinCorrectionDeficit = 0.5f;

    /// <summary>The restore is on unless explicitly switched off.</summary>
    public static bool Enabled =>
        !string.Equals(Environment.GetEnvironmentVariable(EnvKillSwitch), "0", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Decides whether a hull crossing a seam should be re-driven, and at what forward speed.
    /// </summary>
    /// <param name="enabled">Result of <see cref="Enabled"/>; split out so callers and tests can gate cheaply.</param>
    /// <param name="measuredSpeed">Last speed measured from the simulator's reported positions, in m/s.</param>
    /// <param name="speedAgeMs">Age of that measurement.</param>
    /// <param name="throttle">Current helm throttle; way off means the rider chose to slow down.</param>
    /// <remarks>
    /// There is deliberately no check against the hull's model speed. That figure is the simulator's
    /// thrust cut-off rather than a limit (see
    /// <see cref="Models.Game.Units.Movements.ShipPoseSeed.EffectiveMaxVelocity"/>), so a hull reporting
    /// more than it is not evidence of a corrupt reading, and refusing those restores turned away
    /// precisely the fast crossings that most needed one. <see cref="MaxRestoredSpeed"/> remains as a
    /// sanity ceiling against a saturated reading.
    /// </remarks>
    public static bool TryBuildForwardVelocity(
        bool enabled, float measuredSpeed, long speedAgeMs, sbyte throttle, out float speed)
    {
        speed = 0f;
        if (!enabled)
            return false;
        if (measuredSpeed < MinCruiseSpeed)
            return false;
        if (speedAgeMs >= FreshnessWindowMs)
            return false;
        if (throttle == 0)
            return false;

        speed = Math.Min(measuredSpeed * RestoreFactor, MaxRestoredSpeed);
        return true;
    }

    /// <summary>
    /// Decides how much way a hull is missing on the far side of a seam, from the speed it arrived with
    /// and the first speed the new simulator reports for it.
    /// </summary>
    /// <remarks>
    /// This is the closed-loop form of the restore and is preferred over
    /// <see cref="TryBuildForwardVelocity"/>, because the impulse channel is additive: sending the whole
    /// cruising speed on top of a body that already carries part of it overshoots by whatever the seed
    /// delivered, and sending nothing leaves the hull short by the remainder. Neither can be got right
    /// open-loop, since the surviving fraction is not fixed. Measuring the gap needs no such constant.
    /// </remarks>
    /// <param name="targetSpeed">Speed the hull was making when it was handed over.</param>
    /// <param name="reportedSpeed">Speed the new simulator reports in its first pose.</param>
    /// <param name="thrustCutoff">
    /// The speed above which the hull's own thrust stops (<c>ship_models.velocity × move_speed_mul</c>).
    /// A hull already beyond it is coasting rather than being driven, and re-driving it every crossing
    /// ratchets it to a speed its physics never supports — measured live at 21 m/s on a hull whose
    /// cut-off was 6. Pass zero when it is not known, which skips the check.
    /// </param>
    public static bool TryBuildSeamCorrection(
        bool enabled, float targetSpeed, float reportedSpeed, float thrustCutoff, out float deficit)
    {
        deficit = 0f;
        if (!enabled)
            return false;
        if (targetSpeed < MinCruiseSpeed)
            return false;
        if (reportedSpeed < MinCruiseSpeed)
            return false;

        // Only recover the handover loss of a hull under power. Above its cut-off the engine contributes
        // nothing, so the pose seed's momentum is the whole of what should carry across.
        if (thrustCutoff > 0f && targetSpeed > thrustCutoff)
            return false;

        var shortfall = targetSpeed - reportedSpeed;
        if (shortfall < MinCorrectionDeficit)
            return false;

        deficit = Math.Min(shortfall, MaxRestoredSpeed);
        return true;
    }

    /// <summary>
    /// Packet vectors for a forward-speed restore: magnitude on local +Y (the axis every authored
    /// drive impulse uses, which the dedicate's own rotation turns into bow direction), nothing on
    /// any other channel.
    /// </summary>
    /// <remarks>
    /// The velocity channel of this message is a change in velocity, not a target: the receiver applies
    /// it on top of whatever the body is already doing, and nothing downstream converges an overshoot
    /// back. The seed is what the arm-time flush puts on that body; sending the full cruising speed on
    /// top of a body that already carries the seed is the overshoot that was measured (10.6 m/s handed
    /// over, 15.9 arrived), which is why the correction sends the measured deficit only.
    /// </remarks>
    public static void BuildVectors(float speed, float[] vel, float[] angVel, float[] impulse, float[] angImpulse)
    {
        Array.Clear(vel, 0, vel.Length);
        Array.Clear(angVel, 0, angVel.Length);
        Array.Clear(impulse, 0, impulse.Length);
        Array.Clear(angImpulse, 0, angImpulse.Length);
        vel[1] = speed;
    }
}
