namespace AAEmu.Game.Models.Game.Trading;

public sealed class FreshnessGroupItem
{
    public uint Id { get; init; }
    public uint FreshnessGroupId { get; init; }
    public uint TimeSeconds { get; init; }
    public uint RewardRate { get; init; }
    public int? SellerShareRatio { get; init; }
}
