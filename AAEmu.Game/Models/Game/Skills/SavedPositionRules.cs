using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Where a <c>save_pos</c> buff remembered its owner standing when the marking skill was cast.
/// </summary>
/// <remarks>
/// <c>buffs.save_pos</c> is the client's "this skill recalls me to where I was" flag. Eight rows carry
/// it: the 급습 marking buffs (24610, 24947, 24770, 27825, 27827) and the magic-circle ones
/// (19037 마법진 이동 가능, 25850, 25851). <c>move_to_saved_pos</c> (special type 172) is the other half:
/// 41487/41964/45772/45773/45778/45779 급습 and 42012/43464/43465 마법진 이동 name the marking buff in
/// <c>value1</c> and teleport the caster back to what it captured.
/// <para>
/// Plot-only blinks apply the marking buff <em>after</em> <c>move_to_ground</c>, so
/// <see cref="PreferCastOrigin"/> must prefer <see cref="Skill.CastOrigin"/> (snapshot at
/// <see cref="Skill.Use"/>) over the standing Transform at buff attach.
/// </para>
/// </remarks>
public readonly record struct SavedPosition(
    uint ZoneId,
    uint InstanceId,
    float X,
    float Y,
    float Z,
    float YawRad);

/// <summary>
/// The guards for a recall to a remembered point (testable, no side effects).
/// </summary>
public static class SavedPositionRules
{
    /// <summary>
    /// True when a recall to <paramref name="saved"/> is allowed from the unit's current zone.
    /// </summary>
    /// <remarks>
    /// The marking buff lives on the unit, not on the world, so the point it holds can outlive the
    /// zone it was taken in (a 급습 marked in a housing instance, a magic circle and a walk through a
    /// portal). The landing is applied as a same-zone teleport — it writes the transform and tells the
    /// zone, but never re-resolves the zone from the coordinates — so a point from another zone or
    /// instance has to be refused rather than written as if it were local.
    /// </remarks>
    public static bool CanReturnTo(SavedPosition? saved, uint currentZoneId, uint currentInstanceId)
        => saved.HasValue
           && saved.Value.ZoneId == currentZoneId
           && saved.Value.InstanceId == currentInstanceId;

    /// <summary>
    /// Builds the mark from the owner's current transform (continent XYZ + zone + instance + yaw).
    /// </summary>
    public static SavedPosition Capture(BaseUnit owner)
    {
        var world = owner.Transform.World;
        return new SavedPosition(
            owner.Transform.ZoneId,
            owner.Transform.InstanceId,
            world.Position.X,
            world.Position.Y,
            world.Position.Z,
            world.Rotation.Z);
    }

    /// <summary>
    /// Snapshot at skill <c>Use</c>, before the plot graph moves the caster.
    /// </summary>
    public static SavedPosition FromUnit(Unit unit) => Capture(unit);

    /// <summary>
    /// Prefers the cast-start mark when the skill recorded one; otherwise the standing Transform
    /// (skill_effects that apply save_pos before the leap).
    /// </summary>
    public static SavedPosition PreferCastOrigin(SavedPosition? castOrigin, BaseUnit standingNow) =>
        castOrigin ?? Capture(standingNow);
}
