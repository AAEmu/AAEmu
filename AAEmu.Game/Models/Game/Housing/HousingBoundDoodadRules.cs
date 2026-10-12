namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Bound house doodads (doors, NPCs, worldgate) must be tracked on the house whenever they are
/// loaded from DB — otherwise reconcile treats them as missing and spawns a second set on top.
/// Persistence is a separate decision: only the global flag or a guild worldgate writes to MySQL
/// when the flag is off.
/// </summary>
public static class HousingBoundDoodadRules
{
    /// <summary>
    /// Parent a DB-loaded housing doodad into <c>House.AttachedDoodads</c> when it matches a
    /// template binding. Always — tracking is required for reconcile idempotency.
    /// </summary>
    public static bool ShouldTrackBoundOnHouse(bool isTemplateBinding) => isTemplateBinding;

    /// <summary>
    /// Whether a newly created binding should be saved to MySQL.
    /// </summary>
    public static bool ShouldPersistBinding(bool usePersistentHouseDoodads, bool isWorldgate) =>
        usePersistentHouseDoodads || isWorldgate;
}
