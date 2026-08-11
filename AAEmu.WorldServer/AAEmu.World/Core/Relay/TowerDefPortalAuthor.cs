namespace AAEmu.World.Core.Relay;

/// <summary>
/// Intentionally unused stub. Tower-def portals must come from zone OnEvent/ZW, not World mesh authoring.
/// </summary>
public static class TowerDefPortalAuthor
{
    public static void ArmStart(
        AAEmu.Game.Models.Game.TowerDefs.TowerDef towerDef,
        IReadOnlyList<uint> hostZoneIds,
        IReadOnlyDictionary<uint, uint> spotByZone)
    {
    }

    public static void ArmWave(
        AAEmu.Game.Models.Game.TowerDefs.TowerDef towerDef,
        int step,
        IReadOnlyList<uint> hostZoneIds,
        IReadOnlyDictionary<uint, uint> spotByZone)
    {
    }

    public static void ClearRun(uint towerDefId, string reason)
    {
    }
}
