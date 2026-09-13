namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Plot dimensions shared by housing templates through <c>housings.housing_size_id</c>.
/// </summary>
public sealed class HousingSize
{
    public uint Id { get; init; }
    public ushort ButlerGardenSize { get; init; }
    public float GardenRadius { get; init; }
    public uint HousingViewSizeId { get; init; }
}
