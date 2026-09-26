using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Content-backed admission for Farmhand gardening, harvest, and specialty-trade registration. Callers hold the
/// Farmhand operation and state locks; this resolver does not acquire Farmhand, house, inventory, or database locks.
/// </summary>
public sealed class ButlerFarmingAdmissionResolver :
    IButlerFarmingAdmissionResolver,
    IButlerGardenStorageResolver,
    IButlerChargeContextResolver
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // CSSwapButlerItem uses the Inventory namespace byte for stored Farmhand garden records.
    private const byte GardenStorageType = (byte)SlotType.Inventory;

    private readonly ButlerGameData _butlerGameData;
    private readonly Func<uint, ButlerGardenTemplate?> _findGardenTemplate;
    private readonly IItemManager _itemManager;
    private readonly ICraftManager _craftManager;
    private readonly ISkillManager _skillManager;
    private readonly Func<IHousingManager> _housingManager;
    private readonly Func<IZoneManager> _zoneManager;

    public ButlerFarmingAdmissionResolver()
        : this(
            ButlerGameData.Instance,
            itemTemplateId => HousingGameData.Instance.TryGetButlerGardenTemplate(itemTemplateId, out var template)
                ? template
                : null,
            ItemManager.Instance,
            CraftManager.Instance,
            SkillManager.Instance)
    {
    }

    internal ButlerFarmingAdmissionResolver(
        ButlerGameData butlerGameData,
        Func<uint, ButlerGardenTemplate?> findGardenTemplate,
        IItemManager itemManager)
        : this(butlerGameData, findGardenTemplate, itemManager, null, null, null, null)
    {
    }

    internal ButlerFarmingAdmissionResolver(
        ButlerGameData butlerGameData,
        Func<uint, ButlerGardenTemplate?> findGardenTemplate,
        IItemManager itemManager,
        ICraftManager craftManager,
        ISkillManager skillManager)
        : this(butlerGameData, findGardenTemplate, itemManager, craftManager, skillManager, null, null)
    {
    }

    internal ButlerFarmingAdmissionResolver(
        ButlerGameData butlerGameData,
        Func<uint, ButlerGardenTemplate?> findGardenTemplate,
        IItemManager itemManager,
        ICraftManager craftManager,
        ISkillManager skillManager,
        Func<IHousingManager> housingManager = null,
        Func<IZoneManager> zoneManager = null)
    {
        _butlerGameData = butlerGameData ?? throw new ArgumentNullException(nameof(butlerGameData));
        _findGardenTemplate = findGardenTemplate ?? throw new ArgumentNullException(nameof(findGardenTemplate));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _craftManager = craftManager;
        _skillManager = skillManager;
        _housingManager = housingManager ?? (() => ResolveFromContainer<IHousingManager>());
        _zoneManager = zoneManager ?? (() => ResolveFromContainer<IZoneManager>());
    }

    private static T ResolveFromContainer<T>() where T : class =>
        SingletonContainer.ServiceProvider?.GetService<T>();

    public bool TryResolveGarden(uint itemTemplateId, out ButlerGardenStorageItem garden)
    {
        garden = default;
        var template = _findGardenTemplate(itemTemplateId);
        if (template is null)
            return false;

        var resolved = template.Value;
        if (resolved.ItemId != itemTemplateId || resolved.ButlerHarvestGradeId == 0 ||
            !_butlerGameData.TryGetHarvestGrade(resolved.ButlerHarvestGradeId, out var harvestGrade) ||
            harvestGrade.Grade == 0)
            return false;

        garden = new ButlerGardenStorageItem(
            resolved.ItemId,
            resolved.GardenSize,
            resolved.IsUnderWater,
            harvestGrade.Grade);
        return true;
    }

    public bool TryResolveGardenStorage(CharacterButler butler, out ButlerGardenStorageState state)
    {
        state = default;
        if (!TryResolveCurrentLevel(butler, out _, out var level) ||
            !_butlerGameData.TryGetHarvestGrade(level.ButlerHarvestGradeId, out var currentHarvestGrade) ||
            currentHarvestGrade.Grade == 0 || butler.StoredItems.Count > level.TotalGardenCount)
            return false;

        try
        {
            uint landGardenSize = 0;
            uint waterGardenSize = 0;
            foreach (var stored in butler.StoredItems.Values)
            {
                if (!TryResolveStoredGarden(butler, stored, out var garden))
                    return false;

                if (garden.IsUnderWater)
                    waterGardenSize = checked(waterGardenSize + garden.GardenSize);
                else
                    landGardenSize = checked(landGardenSize + garden.GardenSize);
            }

            uint activeLandGardenSize = 0;
            uint activeWaterGardenSize = 0;
            foreach (var job in butler.HarvestJobs.Values)
            {
                if (!_butlerGameData.TryGetHarvest(job.StaticHarvestId, out var harvest) ||
                    harvest is not { Id: > 0, Size: > 0, IsUnderWater: not null })
                    return false;

                var jobGardenSize = checked((uint)((ulong)job.RequestedAmount * harvest.Size.Value));
                if (harvest.IsUnderWater.Value)
                    activeWaterGardenSize = checked(activeWaterGardenSize + jobGardenSize);
                else
                    activeLandGardenSize = checked(activeLandGardenSize + jobGardenSize);
            }

            if (activeLandGardenSize > landGardenSize || activeWaterGardenSize > waterGardenSize)
                return false;

            state = new ButlerGardenStorageState(
                level,
                currentHarvestGrade.Grade,
                checked((uint)butler.StoredItems.Count),
                landGardenSize,
                waterGardenSize,
                activeLandGardenSize,
                activeWaterGardenSize);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public bool TryResolveHarvest(
        Character character,
        CharacterButler butler,
        uint staticHarvestId,
        out ButlerHarvestAdmissionContext context)
    {
        context = default;
        if (character == null || butler == null || character.Id != butler.CharacterId ||
            !_butlerGameData.TryGetUniqueTemplate(out var butlerTemplate) ||
            !TryResolveCurrentLevel(butler, out _, out var level) ||
            !_butlerGameData.TryGetHarvestGrade(level.ButlerHarvestGradeId, out var currentHarvestGrade) ||
            !_butlerGameData.TryGetHarvest(staticHarvestId, out var harvest) ||
            !_butlerGameData.TryGetHarvestGrade(harvest.ButlerHarvestGradeId, out var requiredHarvestGrade) ||
            harvest.ConsumeLp is null ||
            !TryResolveGardenStorage(butler, out var gardenStorage))
            return false;

        context = new ButlerHarvestAdmissionContext(
            butlerTemplate,
            level,
            currentHarvestGrade,
            requiredHarvestGrade,
            harvest,
            new ButlerFarmingResources(
                0,
                butler.LaborPower,
                gardenStorage.LandGardenSize - gardenStorage.ActiveLandGardenSize,
                gardenStorage.WaterGardenSize - gardenStorage.ActiveWaterGardenSize,
                butler.RemainProductionCost),
            harvest.ConsumeLp.Value,
            ItemTaskType.RequestButlerHarvestRegister,
            butler.SpecialtyTradeJobs.Count > 0);
        return true;
    }

    public bool TryResolveSpecialtyTrade(
        Character character,
        CharacterButler butler,
        uint specialtyType,
        short toZoneGroupType,
        out ButlerSpecialtyTradeAdmissionContext context) =>
        TryResolveSpecialtyTrade(character, butler, specialtyType, toZoneGroupType, out context, out _);

    public bool TryResolveSpecialtyTrade(
        Character character,
        CharacterButler butler,
        uint specialtyType,
        short toZoneGroupType,
        out ButlerSpecialtyTradeAdmissionContext context,
        out ButlerSpecialtyTradeRules.AdmissionFailure failure)
    {
        context = default;
        failure = ButlerSpecialtyTradeRules.AdmissionFailure.InvalidContent;
        if (character == null || butler == null || character.Id != butler.CharacterId ||
            specialtyType == 0 || toZoneGroupType <= 0 || _craftManager == null || _skillManager == null ||
            !_butlerGameData.TryGetUniqueTemplate(out var butlerTemplate) ||
            !TryResolveCurrentLevel(butler, out _, out var level) ||
            !_butlerGameData.TryGetSpecialtyTrade(specialtyType, checked((ushort)toZoneGroupType),
                out var trade) ||
            !_craftManager.TryGetCraft(trade.CraftId, out var craft) ||
            craft == null || craft.SkillId == 0 || _skillManager.GetSkillTemplate(craft.SkillId) == null)
            return false;

        var skill = _skillManager.GetSkillTemplate(craft.SkillId);
        // The origin check runs before the slot and duplicate checks: a request for another
        // region's specialty is refused as such whatever the farmhand's slot usage is.
        if (!IsOriginRegionAdmitted(butler, trade.ZoneGroupId))
        {
            failure = ButlerSpecialtyTradeRules.AdmissionFailure.OriginRegionMismatch;
            return false;
        }
        var expandedSlotCount = butler.PermanentDatas.GetValueOrDefault(
            ButlerFarmingService.SpecialtyTradeSlotExpansionPermanentDataKey);
        if (expandedSlotCount > uint.MaxValue)
            return false;
        if (expandedSlotCount > 0 &&
            !_butlerGameData.TryGetTradeSlotExpansionByTotalCount(butlerTemplate.Id,
                checked((uint)expandedSlotCount), out _))
            return false;
        return ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
            butlerTemplate,
            level,
            trade,
            craft,
            skill,
            butler.SpecialtyTradeJobs.Values.ToArray(),
            butlerTemplate.DefaultSpecialtyTradeSlotCount,
            checked((uint)expandedSlotCount),
            out context,
            out failure);
    }

    public bool TryResolveNextGardenSlotExpansion(
        Character character,
        CharacterButler butler,
        out ButlerGardenSlotExpansionContext context)
    {
        context = default;
        if (character == null || butler == null || character.Id != butler.CharacterId ||
            !_butlerGameData.TryGetUniqueTemplate(out var butlerTemplate) ||
            !TryResolveCurrentLevel(butler, out _, out var level) ||
            !TryGetNextGardenSlotExpansion(butler, butlerTemplate.Id, out var expansion) ||
            expansion.Level > level.Level)
            return false;

        // The client names item task 188 UpdateButlerPermanentDatas. Garden expansion persists permanent key 2,
        // so this uses that exact task identity for the consumed expansion item.
        context = new ButlerGardenSlotExpansionContext(
            expansion,
            butlerTemplate.Id,
            level.Level,
            ItemTaskType.UpdateButlerPermanentDatas);
        return true;
    }

    public bool TryResolveNextSpecialtyTradeSlotExpansion(
        Character character,
        CharacterButler butler,
        out ButlerSpecialtyTradeSlotExpansionContext context)
    {
        context = default;
        if (character == null || butler == null || character.Id != butler.CharacterId ||
            !_butlerGameData.TryGetUniqueTemplate(out var butlerTemplate) ||
            !TryResolveCurrentLevel(butler, out _, out var level))
            return false;

        var expandedSlots = butler.PermanentDatas.GetValueOrDefault(
            ButlerFarmingService.SpecialtyTradeSlotExpansionPermanentDataKey);
        if (expandedSlots >= uint.MaxValue ||
            !_butlerGameData.TryGetTradeSlotExpansionByTotalCount(butlerTemplate.Id,
                checked((uint)expandedSlots + 1), out var expansion) ||
            expansion.Level > level.Level)
            return false;

        context = new ButlerSpecialtyTradeSlotExpansionContext(
            expansion,
            butlerTemplate.Id,
            level.Level,
            ItemTaskType.UpdateButlerPermanentDatas);
        return true;
    }

    private bool IsOriginRegionAdmitted(CharacterButler butler, uint destinationZoneGroupId)
    {
        // Both lookups are resolved on demand: touching HousingManager or ZoneManager from the
        // constructor would pull the world and housing graphs in before the container is built.
        var housingManager = _housingManager?.Invoke();
        var zoneManager = _zoneManager?.Invoke();
        if (housingManager == null || zoneManager == null || butler?.HouseId is null or 0)
        {
            // No bound house or no geography to compare against: refuse rather than admit blind.
            Logger.Error(
                "Refusing farmhand specialty trade for character {0}: no bound house region to compare against",
                butler?.CharacterId);
            return false;
        }

        var house = housingManager.GetHouseById(butler.HouseId);
        var houseZoneId = house?.Transform.ZoneId ?? 0u;
        var houseContinentId = houseZoneId == 0 ? 0u : zoneManager.GetTargetIdByZoneId(houseZoneId);
        var destinationContinentId = zoneManager.GetZoneGroupById(destinationZoneGroupId)?.TargetId ?? 0u;
        if (houseContinentId == 0 || destinationContinentId == 0)
        {
            Logger.Error(
                "Refusing farmhand specialty trade for character {0}: house zone {1} continent {2} or destination group {3} continent {4} is unresolved",
                butler.CharacterId, houseZoneId, houseContinentId, destinationZoneGroupId, destinationContinentId);
            return false;
        }

        if (ButlerSpecialtyTradeRules.IsSameOriginRegion(houseContinentId, destinationContinentId))
            return true;

        Logger.Warn(
            "Refused farmhand specialty trade for character {0}: house continent {1} does not own destination group {2} continent {3}",
            butler.CharacterId, houseContinentId, destinationZoneGroupId, destinationContinentId);
        return false;
    }

    private bool TryGetNextGardenSlotExpansion(
        CharacterButler butler,
        uint butlerTemplateId,
        out ButlerSlotExpansion expansion)
    {
        expansion = null;
        var expandedSlots = butler.PermanentDatas.GetValueOrDefault(
            ButlerFarmingService.HarvestSlotExpansionPermanentDataKey);
        return expandedSlots < uint.MaxValue &&
               _butlerGameData.TryGetGardenSlotExpansionByTotalCount(
                   butlerTemplateId,
                   (uint)expandedSlots + 1,
                   out expansion);
    }

    public bool TryResolve(Character character, CharacterButler butler, out ButlerChargeContext context)
    {
        context = default;
        return character != null && butler != null && character.Id == butler.CharacterId &&
               TryResolve(butler, out context);
    }

    public bool TryResolve(CharacterButler butler, out ButlerChargeContext context)
    {
        context = default;
        if (butler == null || !TryResolveCurrentLevel(butler, out var template, out var level) ||
            level.MaxLaborPower == 0 || template.LpChargeRate is not > 0 ||
            template.MaxProductionCost is not > 0 || template.MaxProductionCost.Value > ushort.MaxValue)
            return false;

        try
        {
            context = new ButlerChargeContext(
                level.MaxLaborPower,
                template.LpChargeRate.Value,
                template.MaxProductionCost.Value,
                ButlerContentConfig.RequireChargeLimits());
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryResolveCurrentLevel(CharacterButler butler, out ButlerTemplate template, out ButlerLevel level)
    {
        template = null;
        level = null;
        return butler != null && _butlerGameData.TryGetUniqueTemplate(out template) &&
               _butlerGameData.TryGetLevelForCumulativeExperience(
                   template.Id,
                   butler.PermanentDatas.GetValueOrDefault(ButlerProgression.CumulativeExperiencePermanentDataKey),
                   out level);
    }

    private bool TryResolveStoredGarden(
        CharacterButler butler,
        ButlerStoredItem stored,
        out ButlerGardenStorageItem garden)
    {
        garden = default;
        if (stored.Type != GardenStorageType || stored.ItemId == 0)
            return false;

        var item = _itemManager.GetItemByItemId(stored.ItemId);
        return item is { Count: 1, SlotType: SlotType.System } && item.OwnerId == butler.CharacterId &&
               TryResolveGarden(item.TemplateId, out garden);
    }
}
