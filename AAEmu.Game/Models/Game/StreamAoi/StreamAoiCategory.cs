namespace AAEmu.Game.Models.Game.StreamAoi;

/// <summary>
/// Soft SCUnitState interest bands. Region neighborhood is still the coarse pool;
/// these radii decide paint vs <c>SCUnitsRemoved</c>.
/// </summary>
public enum StreamAoiCategory : byte
{
    /// <summary>
    /// Players, farm haulers, and normal NPCs. Measured: appear ~105 m, gone ~110 m.
    /// </summary>
    Ambient = 0,

    /// <summary>
    /// Sea bosses (Kraken / Leviathan NPC). Same band as ships: appear ~225 m, gone ~248 m.
    /// </summary>
    Large = 1,

    /// <summary>
    /// Sea hulls (schooner, clipper, other boats). Appear ~225 m, gone ~248 m.
    /// Selectable Slave only — attached sail/cannon doodads stream separately.
    /// </summary>
    Ship = 2,

    /// <summary>
    /// Tower hellgates / event seeds. Appear/gone ~700 m.
    /// </summary>
    Event = 3,

    /// <summary>
    /// Post-1.7 ship parts (sail/cannon doodads and SlaveKind equipment). Not the selectable hull.
    /// Soft unit AOI does not apply — they linger after the hull unselects until region leave.
    /// </summary>
    Part = 4
}
