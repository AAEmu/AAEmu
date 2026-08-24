namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// One row of SCInstanceVisitCounts / SCInstanceVisitCountChange (20-byte elem).
/// </summary>
public readonly record struct InstanceVisitCountRecord(
    int ZoneGroupId,
    uint InstanceCatalogId,
    int UsedCount,
    int ResetCount,
    int PermittedCount);
