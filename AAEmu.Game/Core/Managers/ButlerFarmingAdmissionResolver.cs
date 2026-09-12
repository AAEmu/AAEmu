using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Content-backed admission for Farmhand gardening and harvest registration. Callers hold the Farmhand operation
/// and state locks; this resolver does not acquire Farmhand, house, inventory, or database locks.
/// </summary>
public sealed class ButlerFarmingAdmissionResolver :
    IButlerFarmingAdmissionResolver,
    IButlerGardenStorageResolver,
    IButlerChargeContextResolver
{
    // CSSwapButlerItem uses the Inventory namespace byte for stored Farmhand garden records.
    private const byte GardenStorageType = (byte)SlotType.Inventory;

    private readonly ButlerGameData _butlerGameData;
    private readonly Func<uint, ButlerGardenTemplate?> _findGardenTemplate;
    private readonly IItemManager _itemManager;

    public ButlerFarmingAdmissionResolver()
        : this(
            ButlerGameData.Instance,
            itemTemplateId => HousingGameData.Instance.TryGetButlerGardenTemplate(itemTemplateId, out var template)
                ? template
                : null,
            ItemManager.Instance)
    {
    }

    internal ButlerFarmingAdmissionResolver(
        ButlerGameData butlerGameData,
        Func<uint, ButlerGardenTemplate?> findGardenTemplate,
        IItemManager itemManager)
    {
        _butlerGameData = butlerGameData ?? throw new ArgumentNullException(nameof(butlerGameData));
        _findGardenTemplate = findGardenTemplate ?? throw new ArgumentNullException(nameof(findGardenTemplate));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
    }

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
            false);
        return true;
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
            !_butlerGameData.TryGetGardenSlotExpansion(butlerTemplate.Id, level.Level, out var expansion))
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
