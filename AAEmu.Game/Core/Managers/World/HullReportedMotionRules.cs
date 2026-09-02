namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Decides whether the velocity a zone reports for a hull describes motion that hull is actually
/// making, so a pose seed never hands a hull its own unspent velocity back.
/// </summary>
/// <remarks>
/// A zone reads hull velocity out of the physics entity's own velocity field, and that field
/// outlives the motion it described: once an entity stops being stepped, its position stops
/// advancing while the last velocity written to it keeps being reported. Seeding that figure back
/// closes a loop — the seed writes the velocity, the report echoes it, the next seed carries it
/// again — and the simulator spends it as coast braking the moment a helm arms. On a hull with no
/// rudder force that brake is the only force acting, and it enters the solver as a horizontal
/// component of the gravity vector, so a hull that is standing still tilts and keeps tilting.
///
/// World already holds the two figures that tell real way from a leftover: the speed measured from
/// successive reported positions (<see cref="Models.Game.Units.Slave.SimulatedSpeed"/>) and the
/// speed decoded from the report's own velocity fields
/// (<see cref="Models.Game.Units.Movements.ShipMoveType.ReportedSpeed"/>). A report claiming way
/// while the hull has not travelled is not a measurement of the hull.
/// </remarks>
public static class HullReportedMotionRules
{
    /// <summary>
    /// Least fraction of the reported speed the travelled distance must show for the report to be
    /// believed.
    /// </summary>
    /// <remarks>
    /// The measured figure is an average over a sampling window while the reported one is
    /// instantaneous, so a hull under thrust legitimately measures lower than it reports: accelerating
    /// uniformly across a window averages about half the end speed, and that is the worst honest case.
    /// A quarter leaves room for a harder ramp than any hull can make while still rejecting a hull
    /// that has not moved at all, which is what a leftover velocity looks like — travel three orders
    /// of magnitude below the reported speed.
    /// </remarks>
    public const float MinCorroboratedFraction = 0.25f;

    /// <summary>
    /// Same window the seam impulse treats as "this speed is no longer the hull". With no
    /// measurement this fresh there is nothing to check the report against.
    /// </summary>
    public static long FreshnessWindowMs => BoatSeamImpulse.FreshnessWindowMs;

    /// <summary>
    /// Whether the travelled distance backs up the velocity the zone reported.
    /// </summary>
    /// <param name="reportedSpeed">Speed decoded from the report's velocity fields, in m/s.</param>
    /// <param name="measuredSpeed">Speed measured from successive reported positions, in m/s.</param>
    /// <param name="measuredAgeMs">
    /// Age of that measurement. A stale or absent measurement corroborates nothing either way, and
    /// is treated as agreement so a hull is never held back on missing evidence.
    /// </param>
    public static bool IsReportedMotionCorroborated(
        float reportedSpeed, float measuredSpeed, long measuredAgeMs)
    {
        if (!float.IsFinite(reportedSpeed) || reportedSpeed <= 0f)
            return true;
        if (!float.IsFinite(measuredSpeed) || measuredAgeMs < 0 || measuredAgeMs >= FreshnessWindowMs)
            return true;

        return measuredSpeed >= reportedSpeed * MinCorroboratedFraction;
    }
}
