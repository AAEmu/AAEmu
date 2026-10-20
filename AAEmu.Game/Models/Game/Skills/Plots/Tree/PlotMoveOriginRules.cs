namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Pose a plot should keep when a skill controller asks the following events to wait until the move finishes.
/// </summary>
/// <remarks>
/// The wait is <c>skill_controllers.value5 == 1</c> (the delay is value3). Area searches that run after
/// that wait otherwise measure from the caster's landing, so a cone drawn at the shot finds nothing.
/// A search whose stand is some other unit keeps the live caster as its cone origin.
/// </remarks>
public static class PlotMoveOriginRules
{
    /// <summary>A stand this close to the captured pose is still that pose (millimetre plot nudges included).</summary>
    public const float StandOnOriginMetres = 1f;

    public static bool RemembersOrigin(int useDelayFlag) => useDelayFlag == 1;

    public static bool PinsCasterStand(bool basisIsCaster, bool hasOrigin) => basisIsCaster && hasOrigin;

    public static bool StandSitsOnOrigin(bool hasOrigin, float standX, float standY, float originX, float originY)
    {
        if (!hasOrigin)
            return false;
        var dx = standX - originX;
        var dy = standY - originY;
        return dx * dx + dy * dy <= StandOnOriginMetres * StandOnOriginMetres;
    }

    /// <summary>
    /// A cone whose stand still sits on the captured pose is aimed from that stand. One aimed at
    /// another unit stays on <see cref="PlotTargetInfo"/>'s caster origin.
    /// </summary>
    public static bool ConeSearchesFromStand(bool standSitsOnOrigin) => standSitsOnOrigin;
}
