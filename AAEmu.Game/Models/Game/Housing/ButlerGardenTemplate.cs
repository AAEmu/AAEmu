namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Farmhand garden metadata resolved from an item-housing design and its housing-size row.
/// </summary>
public readonly record struct ButlerGardenTemplate(
    uint ItemId,
    uint HousingId,
    uint ButlerHarvestGradeId,
    ushort GardenSize,
    bool IsUnderWater);
