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
    uint BaseProductionCost,
    uint OverworkProductionCost,
    uint TotalProductionCost,
    IReadOnlyList<ButlerSpecialtyTradeMaterialCost> Materials);

public readonly record struct ButlerSpecialtyTradeMaterialCost(uint ItemId, uint Amount);

public static class ButlerSpecialtyTradeRules
{
    // butlers.overwork_production_cost_mul is a per-mille scale, matching the
    // harvest rule in ButlerFarmingRules (x2ui/butler/tab_farming.lua:412-433
    // divides by 1000 and rounds up).
    public const uint OverworkProductionCostScale = 1_000;

    /// <summary>Why an admission was refused, so callers can report the real cause.</summary>
    public enum AdmissionFailure
    {
        None = 0,
        /// <summary>Content or argument shape is not usable.</summary>
        InvalidContent,
        /// <summary>Every specialty-trade slot is already occupied.</summary>
        NoSpecialtyTradeSlot,
        /// <summary>This specialty type is already trading to that destination.</summary>
        DuplicateSpecialtyTrade,
        /// <summary>
        /// The farmhand's bound house is not on the continent that owns the destination's region.
        /// The farmhand trade UI only offers specialties from the house's own region, so a request
        /// for another region's specialty is a crafted one.
        /// </summary>
        OriginRegionMismatch
    }

    /// <summary>
    /// A farmhand may only trade specialties its bound house's region offers. Continents come from
    /// <c>zone_groups.target_id</c>, which is the same relation the portal reagent check compares.
    /// </summary>
    /// <remarks>
    /// This is the destination side of the rule and it is not, on its own, the whole rule: the
    /// origin a specialty belongs to is carried by <c>crafts.req_doodad_id</c>, the region's
    /// Specialty Workbench, and no shipped table relates that doodad to a zone. A continent check
    /// therefore closes cross-continent origin packs but not a same-continent other-region one.
    /// </remarks>
    /// <param name="boundHouseContinentId">Continent of the bound house's zone.</param>
    /// <param name="destinationContinentId">Continent of the destination zone group.</param>
    /// <returns>
    /// False when either continent is unresolved (0), so a missing row refuses the trade instead of
    /// silently admitting it.
    /// </returns>
    public static bool IsSameOriginRegion(uint boundHouseContinentId, uint destinationContinentId) =>
        boundHouseContinentId > 0 && boundHouseContinentId == destinationContinentId;

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
            defaultSpecialtyTradeSlotCount, 0, out context, out _);

    public static bool TryCreateAdmissionContext(
        ButlerTemplate butlerTemplate,
        ButlerLevel butlerLevel,
        ButlerSpecialtyTradeDefinition trade,
        Craft craft,
        SkillTemplate skill,
        IReadOnlyList<ButlerSpecialtyTradeJob> activeJobs,
        uint defaultSpecialtyTradeSlotCount,
        out ButlerSpecialtyTradeAdmissionContext context,
        out AdmissionFailure failure) =>
        TryCreateAdmissionContext(butlerTemplate, butlerLevel, trade, craft, skill, activeJobs,
            defaultSpecialtyTradeSlotCount, 0, out context, out failure);

    public static bool TryCreateAdmissionContext(
        ButlerTemplate butlerTemplate,
        ButlerLevel butlerLevel,
        ButlerSpecialtyTradeDefinition trade,
        Craft craft,
        SkillTemplate skill,
        IReadOnlyList<ButlerSpecialtyTradeJob> activeJobs,
        uint defaultSpecialtyTradeSlotCount,
        uint expandedSpecialtyTradeSlotCount,
        out ButlerSpecialtyTradeAdmissionContext context) =>
        TryCreateAdmissionContext(butlerTemplate, butlerLevel, trade, craft, skill, activeJobs,
            defaultSpecialtyTradeSlotCount, expandedSpecialtyTradeSlotCount, out context, out _);

    public static bool TryCreateAdmissionContext(
        ButlerTemplate butlerTemplate,
        ButlerLevel butlerLevel,
        ButlerSpecialtyTradeDefinition trade,
        Craft craft,
        SkillTemplate skill,
        IReadOnlyList<ButlerSpecialtyTradeJob> activeJobs,
        uint defaultSpecialtyTradeSlotCount,
        uint expandedSpecialtyTradeSlotCount,
        out ButlerSpecialtyTradeAdmissionContext context,
        out AdmissionFailure failure)
    {
        context = default;
        failure = AdmissionFailure.InvalidContent;
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

        if (activeJobs == null)
            return false;
        if ((ulong)activeJobs.Count >= totalSpecialtyTradeSlotCount)
        {
            failure = AdmissionFailure.NoSpecialtyTradeSlot;
            return false;
        }
        if (activeJobs.Any(job => job is { SpecialtyType: var type, ToZoneGroupType: var zone } &&
                                  type == trade.Id && zone == checked((ushort)trade.ZoneGroupId)))
        {
            failure = AdmissionFailure.DuplicateSpecialtyTrade;
            return false;
        }

        failure = AdmissionFailure.None;

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

    /// <summary>
    /// Registration costs for one specialty trade. Validates content shape and computes the
    /// amounts; the caller compares <see cref="ButlerSpecialtyTradeCosts.TotalProductionCost"/>
    /// against the farmhand's remaining production cost so an insufficient balance is reported as
    /// such rather than as invalid content.
    /// </summary>
    /// <param name="hasOtherProductionRunning">
    /// True when the farmhand already has production work running. Shipped ui_texts
    /// butler_trading_specialties_config_tip (id 11129) and butler_traing_step2_desc (id 11224)
    /// both state that a farmhand already running another production function consumes an extra
    /// production cost. This mirrors the harvest rule, which charges overwork while a specialty
    /// trade is active.
    /// </param>
    public static bool TryCalculateCosts(
        ButlerSpecialtyTradeAdmissionContext context,
        uint availableLaborPower,
        bool hasOtherProductionRunning,
        out ButlerSpecialtyTradeCosts costs)
    {
        costs = default;
        if (context.CraftSkill == null || context.CraftSkill.ConsumeLaborPower <= 0 ||
            context.Trade == null || context.Trade.ConsumeProductionCost == 0 ||
            context.Materials == null || context.Materials.Count == 0 ||
            context.ButlerTemplate == null ||
            availableLaborPower < (uint)context.CraftSkill.ConsumeLaborPower)
            return false;

        uint baseProductionCost = context.Trade.ConsumeProductionCost;
        uint overworkProductionCost = 0;
        uint totalProductionCost;
        try
        {
            checked
            {
                if (hasOtherProductionRunning)
                    overworkProductionCost = DivideRoundUp(
                        (ulong)baseProductionCost * context.ButlerTemplate.OverworkProductionCostMul,
                        OverworkProductionCostScale);
                totalProductionCost = checked(baseProductionCost + overworkProductionCost);
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        var materials = new List<ButlerSpecialtyTradeMaterialCost>(context.Materials.Count);
        foreach (var material in context.Materials)
        {
            if (material.ItemId == 0 || material.Amount == 0)
                return false;
            materials.Add(material);
        }

        costs = new ButlerSpecialtyTradeCosts(
            checked((uint)context.CraftSkill.ConsumeLaborPower),
            baseProductionCost,
            overworkProductionCost,
            totalProductionCost,
            materials);
        return true;
    }

    private static uint DivideRoundUp(ulong value, uint divisor)
    {
        var quotient = value / divisor;
        if (value % divisor != 0)
            quotient++;
        return checked((uint)quotient);
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
