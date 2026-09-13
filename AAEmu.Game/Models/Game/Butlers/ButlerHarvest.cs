namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Static row from <c>butler_harvests</c>.</summary>
public class ButlerHarvest
{
    public uint Id { get; set; }
    public uint? ItemId { get; set; }
    public uint? GrowthTime { get; set; }
    public uint? Size { get; set; }
    public uint? RepeatCount { get; set; }
    public uint? ActabilityGroupId { get; set; }
    public uint? ConsumeLp { get; set; }
    public uint? LootPackId { get; set; }
    public uint? BonusRatio { get; set; }
    public uint? BonusLootPackId { get; set; }
    public uint ButlerHarvestGradeId { get; set; }
    public bool? IsUnderWater { get; set; }
}
