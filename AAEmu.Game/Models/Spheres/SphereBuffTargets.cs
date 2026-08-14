namespace AAEmu.Game.Models.Spheres;

/// <summary>
/// Harbor SphereBuff routing. Compact <c>slave_applicable</c> is the gate: dock regen / Ezi
/// blessing sit on the hull; shipyard-allowed sits on the character.
/// </summary>
public static class SphereBuffTargets
{
    public static bool ApplyToCharacter(bool slaveApplicable) => !slaveApplicable;

    public static bool ApplyToSlave(bool slaveApplicable) => slaveApplicable;
}
