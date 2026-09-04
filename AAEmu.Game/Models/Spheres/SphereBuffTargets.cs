namespace AAEmu.Game.Models.Spheres;

/// <summary>
/// Harbor SphereBuff routing. Compact <c>slave_applicable</c> is the gate: dock regen / Ezi
/// blessing sit on the hull; shipyard-allowed sits on the character.
/// </summary>
public static class SphereBuffTargets
{
    public static bool ApplyToCharacter(bool slaveApplicable) => !slaveApplicable;

    public static bool ApplyToSlave(bool slaveApplicable) => slaveApplicable;

    /// <summary>
    /// Whether the owned-mount pass runs at all. Pets follow <c>and_pet</c> on their own;
    /// a character-only buff with <c>and_pet</c> still has to reach a pet that is already out.
    /// </summary>
    public static bool ApplyToOwnedMounts(bool slaveApplicable, bool andPet) =>
        slaveApplicable || andPet;
}
