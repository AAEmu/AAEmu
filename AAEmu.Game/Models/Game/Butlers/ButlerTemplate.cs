namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Static row from <c>butlers</c>.</summary>
public class ButlerTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint ModelId { get; set; }
    public uint? DefaultGardenSlotCount { get; set; }
    public uint? ResetAllActabilityCost { get; set; }
    public uint? ResetAllActabilityCurrencyId { get; set; }
    public uint? LpChargeRate { get; set; }
    public uint? MaxProductionCost { get; set; }
    public uint DefaultFxGroupId { get; set; }
    public uint TradeAvailableLevel { get; set; }
    public uint OverworkProductionCostMul { get; set; }
    public uint DefaultSpecialtyTradeSlotCount { get; set; }
}
