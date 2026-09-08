namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtyMarketState
{
    public long Revision { get; set; }
    // Nested route dictionaries are keyed by item ID, then zone group ID.
    // Ratios use one-hundredth of a percentage point as their internal unit.
    public Dictionary<uint, Dictionary<uint, int>> PriceRatios { get; init; } = [];
    public Dictionary<uint, Dictionary<uint, int>> DemandRemainders { get; init; } = [];
    public Dictionary<(uint ZoneGroupId, uint TagId), List<SpecialtyMaterialContribution>> MaterialContributions { get; init; } = [];
    public Dictionary<(uint ZoneGroupId, uint TradeGoodId), uint> CargoStock { get; init; } = [];
    public Dictionary<(uint ItemId, uint ZoneGroupId), List<SpecialtyMarketRecord>> Records { get; init; } = [];

    public SpecialtyMarketState Clone() => new()
    {
        Revision = Revision,
        PriceRatios = PriceRatios.ToDictionary(entry => entry.Key, entry => new Dictionary<uint, int>(entry.Value)),
        DemandRemainders = DemandRemainders.ToDictionary(entry => entry.Key, entry => new Dictionary<uint, int>(entry.Value)),
        MaterialContributions = MaterialContributions.ToDictionary(entry => entry.Key,
            entry => entry.Value.Select(contribution => new SpecialtyMaterialContribution(
                contribution.Sequence,
                contribution.ItemId,
                contribution.Amount)).ToList()),
        CargoStock = new(CargoStock),
        Records = Records.ToDictionary(entry => entry.Key,
            entry => entry.Value.Select(record => new SpecialtyMarketRecord(record.Ratio, record.Recorded)).ToList())
    };
}
