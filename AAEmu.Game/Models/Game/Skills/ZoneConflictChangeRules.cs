using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What a zone_conflict_change special effect (type 170) asks the zone group's war state to become.
/// </summary>
/// <remarks>
/// <c>value4</c> is the state and reads straight off <see cref="ZoneConflictType"/>, which the shipped
/// rows confirm: 37734/41472/69422/51166 (전쟁 선포, "declare war") ask for 6 War, 37795/41471/41198
/// (분쟁 선포) for 5 Conflict, 37796/40261/41469 (평화 선포) for 7 Peace, and 37798/41470 (위험 선포) for
/// 0 Tension. <c>value3</c> is the duration the declaration asks for - 5400 s on 13 rows, 180000 on 37793
/// and 37797, 2400 on 51081/51090/51224 and 60 on 59764/59840 - and <c>value2</c> is 1 on 17 rows but 0 on
/// the five that ask for 2400 s or 60 s. Neither has a counterpart in <see cref="ZoneConflict"/>, which is
/// keyed by the zone group the caster stands in and times each state from its own cycle configuration.
/// </remarks>
public static class ZoneConflictChangeRules
{
    /// <summary>
    /// The state a raw <c>value4</c> names, or null when it is not one of the eight states.
    /// </summary>
    public static ZoneConflictType? ResolveState(int value)
        => value >= (int)ZoneConflictType.Tension && value <= (int)ZoneConflictType.Peace
            ? (ZoneConflictType)value
            : null;

    /// <summary>
    /// Whether the declaration is for the state the group is already in. Applying it would be a no-op
    /// (SetState returns without publishing), and the cast's other effects still have to run, so the
    /// caller only skips the state change itself.
    /// </summary>
    public static bool IsChange(ZoneConflictType current, ZoneConflictType requested) => current != requested;
}
