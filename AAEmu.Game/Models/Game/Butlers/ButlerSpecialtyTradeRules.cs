using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Butlers;

public readonly record struct ButlerSpecialtyTradeAdmissionContext(
    ButlerTemplate ButlerTemplate,
    ButlerLevel ButlerLevel,
    ButlerSpecialtyTradeDefinition Trade,
    Craft Craft,
    SkillTemplate CraftSkill,
    CraftProduct Product,
    IReadOnlyList<ButlerSpecialtyTradeMaterialCost> Materials,
    uint AvailableSpecialtyTradeSlots);

public readonly record struct ButlerSpecialtyTradeCosts(
    uint LaborPower,
    uint ProductionCost,
    IReadOnlyList<ButlerSpecialtyTradeMaterialCost> Materials);

public readonly record struct ButlerSpecialtyTradeMaterialCost(uint ItemId, uint Amount);

public static class ButlerSpecialtyTradeRules
{
    public static bool TryCreateAdmissionContext(
        ButlerTemplate butlerTemplate,
        ButlerLevel butlerLevel,
        ButlerSpecialtyTradeDefinition trade,
        Craft craft,
        SkillTemplate skill,
        IReadOnlyList<ButlerSpecialtyTradeJob> activeJobs,
        uint defaultSpecialtyTradeSlotCount,
        out ButlerSpecialtyTradeAdmissionContext context) =>
        TryCreateAdmissionContext(butlerTemplate, butlerLevel, trade, craft, skill, activeJobs,
            defaultSpecialtyTradeSlotCount, 0, out context);

    public static bool TryCreateAdmissionContext(
        ButlerTemplate butlerTemplate,
        ButlerLevel butlerLevel,
        ButlerSpecialtyTradeDefinition trade,
        Craft craft,
        SkillTemplate skill,
        IReadOnlyList<ButlerSpecialtyTradeJob> activeJobs,
        uint defaultSpecialtyTradeSlotCount,
        uint expandedSpecialtyTradeSlotCount,
        out ButlerSpecialtyTradeAdmissionContext context)
    {
        context = default;
        if (butlerTemplate == null || butlerLevel == null || trade == null || craft == null || skill == null ||
            butlerTemplate.Id == 0 || butlerLevel.ButlerId != butlerTemplate.Id ||
            butlerLevel.Level < butlerTemplate.TradeAvailableLevel ||
            trade.Id == 0 || trade.NpcId == 0 || trade.CraftId == 0 || trade.ZoneGroupId == 0 ||
            trade.DeliveryMinTime == 0 || trade.DeliveryMinTime > trade.DeliveryMaxTime ||
            trade.ZoneGroupId > ushort.MaxValue || trade.ConsumeProductionCost == 0 ||
            craft.Id != trade.CraftId || skill.Id != craft.SkillId ||
            skill.ConsumeLaborPower <= 0 || defaultSpecialtyTradeSlotCount == 0)
            return false;

        var totalSpecialtyTradeSlotCount = (ulong)defaultSpecialtyTradeSlotCount + expandedSpecialtyTradeSlotCount;
        if (totalSpecialtyTradeSlotCount > uint.MaxValue)
            return false;
        if (craft.CraftProducts is not { Count: 1 } ||
            craft.CraftProducts[0] is not { Amount: 1, Rate: 100, ItemId: > 0 } product)
            return false;
        if (craft.CraftMaterials is null || craft.CraftMaterials.Count == 0)
            return false;

        var materials = new List<ButlerSpecialtyTradeMaterialCost>(craft.CraftMaterials.Count);
        var seenItems = new HashSet<uint>();
        foreach (var material in craft.CraftMaterials)
        {
            if (material == null || material.ItemId == 0 || material.Amount <= 0 || !seenItems.Add(material.ItemId))
                return false;
            materials.Add(new ButlerSpecialtyTradeMaterialCost(material.ItemId, checked((uint)material.Amount)));
        }

        if (activeJobs == null || (ulong)activeJobs.Count >= totalSpecialtyTradeSlotCount ||
            activeJobs.Any(job => job is { SpecialtyType: var type, ToZoneGroupType: var zone } &&
                                  type == trade.Id && zone == checked((ushort)trade.ZoneGroupId)))
            return false;

        context = new ButlerSpecialtyTradeAdmissionContext(
            butlerTemplate,
            butlerLevel,
            trade,
            craft,
            skill,
            product,
            materials,
            checked((uint)totalSpecialtyTradeSlotCount - (uint)activeJobs.Count));
        return true;
    }

    public static bool TryCalculateCosts(
        ButlerSpecialtyTradeAdmissionContext context,
        uint availableLaborPower,
        uint availableProductionCost,
        out ButlerSpecialtyTradeCosts costs)
    {
        costs = default;
        if (context.CraftSkill == null || context.CraftSkill.ConsumeLaborPower <= 0 ||
            context.Trade == null || context.Trade.ConsumeProductionCost == 0 ||
            context.Materials == null || context.Materials.Count == 0 ||
            availableLaborPower < (uint)context.CraftSkill.ConsumeLaborPower ||
            availableProductionCost < context.Trade.ConsumeProductionCost)
            return false;

        var materials = new List<ButlerSpecialtyTradeMaterialCost>(context.Materials.Count);
        foreach (var material in context.Materials)
        {
            if (material.ItemId == 0 || material.Amount == 0)
                return false;
            materials.Add(material);
        }

        costs = new ButlerSpecialtyTradeCosts(
            checked((uint)context.CraftSkill.ConsumeLaborPower),
            context.Trade.ConsumeProductionCost,
            materials);
        return true;
    }

    public static bool TryChooseDeliveryTime(
        ButlerSpecialtyTradeDefinition trade,
        Func<uint, uint, uint> choose,
        out uint deliveryTime)
    {
        deliveryTime = 0;
        if (trade == null || trade.DeliveryMinTime == 0 || trade.DeliveryMinTime > trade.DeliveryMaxTime ||
            choose == null)
            return false;

        var selected = choose(trade.DeliveryMinTime, trade.DeliveryMaxTime);
        if (selected < trade.DeliveryMinTime || selected > trade.DeliveryMaxTime)
            return false;

        deliveryTime = selected;
        return true;
    }

    public static bool IsDue(ButlerSpecialtyTradeJob job, long nowUnix)
    {
        if (job == null || job.CreatedTime < 0 || job.DeliveryTime == 0 || nowUnix < 0)
            return false;
        try
        {
            return nowUnix >= checked(job.CreatedTime + job.DeliveryTime);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static uint RemainingDeliverySeconds(ButlerSpecialtyTradeJob job, long nowUnix)
    {
        if (job == null || job.CreatedTime < 0 || job.DeliveryTime == 0 || nowUnix < 0)
            return 0;
        try
        {
            var due = checked(job.CreatedTime + job.DeliveryTime);
            return due <= nowUnix ? 0 : checked((uint)(due - nowUnix));
        }
        catch (OverflowException)
        {
            return 0;
        }
    }
}
