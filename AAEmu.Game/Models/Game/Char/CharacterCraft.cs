using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterCraft
{
    public const int MaxBatchCount = 1000;

    private readonly Character _owner;
    private readonly ICraftManager _craftManager;
    private readonly IDoodadManager _doodadManager;
    private readonly ISkillManager _skillManager;
    private readonly IItemManager _itemManager;
    private readonly IZoneManager _zoneManager;

    public CharacterCraft(Character owner)
        : this(
            owner,
            CraftManager.Instance,
            DoodadManager.Instance,
            SkillManager.Instance,
            ItemManager.Instance,
            ZoneManager.Instance)
    {
    }

    public CharacterCraft(
        Character owner,
        ICraftManager craftManager,
        IDoodadManager doodadManager,
        ISkillManager skillManager,
        IItemManager itemManager,
        IZoneManager zoneManager)
    {
        _owner = owner;
        _craftManager = craftManager;
        _doodadManager = doodadManager;
        _skillManager = skillManager;
        _itemManager = itemManager;
        _zoneManager = zoneManager;
    }

    private int Count { get; set; }
    private Craft CurrentCraft { get; set; }
    /// <summary>
    /// Crafter doodad Id
    /// </summary>
    private uint DoodadId { get; set; }
    private int ConsumeLaborPower { get; set; }
    private Skill CurrentSkill { get; set; }
    private uint CraftPackId { get; set; }
    private uint DoodadFuncGroupId { get; set; }
    private uint ProductionZoneGroupId { get; set; }
    private Character Owner => _owner;
    public bool IsCrafting { get; set; }

    public static bool IsValidBatchCount(int count) => count is > 0 and <= MaxBatchCount;

    public bool Craft(Craft craft, int count, uint doodadId)
    {
        lock (Owner.StateSyncRoot)
            return CraftCore(craft, count, doodadId);
    }

    private bool CraftCore(Craft craft, int count, uint doodadId)
    {
        if (IsCrafting)
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.CraftPermissionDeny, 0, false);
            return false;
        }

        if (craft == null || !IsValidBatchCount(count) ||
            !TryResolveCraftSource(craft, doodadId, out var doodad, out var craftPack, out var productionZoneGroupId))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.InvalidTarget, 0, false);
            CancelCraft();
            return false;
        }

        // check if you are equipped with a backpack or glider
        if (!Owner.Inventory.CanReplaceGliderInBackpackSlot())
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.BackpackOccupied, 0, false);
            CancelCraft();
            return false;
        }

        // Check if we have enough materials
        if (!TryGetRequiredBagMaterials(craft, out _))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.NotEnoughRequiredItem, 0, false);
            CancelCraft();
            return false;
        }

        if (!HasCraftPermission(doodad, (DoodadFuncPermission)craftPack.Function.PermId))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.CraftPermissionDeny, 0, false);
            CancelCraft();
            return false;
        }

        CurrentCraft = craft;
        Count = count;
        DoodadId = doodadId;
        DoodadFuncGroupId = doodad.FuncGroupId;
        CraftPackId = craftPack.Template.CraftPackId;
        ProductionZoneGroupId = productionZoneGroupId;
        IsCrafting = true;

        var caster = SkillCaster.GetByType(SkillCasterType.Unit);
        caster.ObjId = Owner.ObjId;

        var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
        target.ObjId = doodadId;

        var skillTemplate = _skillManager.GetSkillTemplate(craft.SkillId);
        if (skillTemplate == null)
        {
            CancelCraft();
            return false;
        }
        var skill = new Skill(skillTemplate);
        ConsumeLaborPower = skill.GetLaborCost(Owner);
        CurrentSkill = skill;
        // 10.0.2.13: Craft.AcId removed; actability-based speed multiplier dropped
        var speedMultiplier = 1f;
        skill.CastTimeMultiplier = speedMultiplier;
        var result = skill.Use(Owner, caster, target, null, false, out _);
        if (result != SkillResult.Success)
        {
            CancelCraft();
            return false;
        }
        return true;
    }

    public bool EndCraft(Skill skill = null)
    {
        lock (Owner.StateSyncRoot)
            return EndCraftCore(skill ?? CurrentSkill);
    }

    private bool EndCraftCore(Skill skill)
    {
        if (!IsCrafting || CurrentCraft == null)
        {
            CancelCraft();
            return false;
        }

        if (!TryResolveCraftSource(CurrentCraft, DoodadId, out var doodad, out var craftPack, out var productionZoneGroupId) ||
            doodad.FuncGroupId != DoodadFuncGroupId ||
            craftPack.Template.CraftPackId != CraftPackId ||
            productionZoneGroupId != ProductionZoneGroupId ||
            !HasCraftPermission(doodad, (DoodadFuncPermission)craftPack.Function.PermId))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.CraftPermissionDeny, 0, false);
            CancelCraft();
            return false;
        }

        if (!TryGetRequiredBagMaterials(CurrentCraft, out var requiredMaterials))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.NotEnoughRequiredItem, 0, false);
            CancelCraft();
            return false;
        }

        if (skill == null || !ReferenceEquals(skill, CurrentSkill) || skill.Template?.Id != CurrentCraft.SkillId ||
            Owner.LaborPower + Owner.LocalLaborPower < ConsumeLaborPower)
        {
            Owner.SendDebugMessage("|cFFFFFF00[Craft] Not enough Labor Powers for crafting! Performing a fictitious crafting step...|r");
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.NotEnoughLaborPower, 0, false);
            CancelCraft();
            return false;
        }

        /*
        // "Proper" Grade inheritance referencing the compact flags, doesn't work for 1.2
        // Find the material that determines the grade for inheritance
        byte inheritedGrade = 0;
        var mainGradeMaterial = CurrentCraft.CraftMaterials.FirstOrDefault(m => m.MainGrade);
        Owner.SendDebugMessage($"Looking for main grade material. Found: {mainGradeMaterial?.ItemId ?? 0}");
        if (mainGradeMaterial != null)
        {
            // Search Bag container for the material  
            Item foundMaterial = null;
            if (Owner.Inventory.Bag.GetAllItemsByTemplate(mainGradeMaterial.ItemId, -1, out var items, out _))
            {
                if (items.Count > 0)
                {
                    foundMaterial = items[0];
                }
            }

            if (foundMaterial != null)
            {
                inheritedGrade = foundMaterial.Grade;
                Owner.SendDebugMessage($"Found material {mainGradeMaterial.ItemId} with grade {inheritedGrade}");
            }
            else
            {
                Owner.SendDebugMessage($"Could not find material {mainGradeMaterial.ItemId} in any container");
            }
        }

        foreach (var product in CurrentCraft.CraftProducts)
        {
            // Determine the grade to use for this product  
            int gradeToUse = -1; // Default grade

            if (product.UseGrade)
            {
                // If UseGrade is true, inherit from main grade material and roll for free regrade  
                gradeToUse = FreeRegrade((int)inheritedGrade);
                Owner.SendDebugMessage($"Product {product.ItemId} will use inherited grade {gradeToUse}");
            }
            else if (product.ItemGradeId > 0)
            {
                // If ItemGradeId is specified, use that grade  
                gradeToUse = (int)product.ItemGradeId;
                Owner.SendDebugMessage($"Product {product.ItemId} will use fixed grade {gradeToUse}");
            }
            else
            {
                Owner.SendDebugMessage($"Product will use default grade: {gradeToUse}");
            }

            // Check if template allows grade changes  
            var template = ItemManager.Instance.GetTemplate(product.ItemId);
            if (template != null)
            {
                Owner.SendDebugMessage($"Product template {product.ItemId} - FixedGrade: {template.FixedGrade}, Gradable: {template.Gradable}");
            }

            // Check if we're crafting a trade pack, if so, try to remove currently equipped backpack slot
            if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId) == false)
            {
                Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CraftActSaved, product.ItemId, product.Amount, gradeToUse, Owner.Id);
            }
            else
            {
                if (!Owner.Inventory.TryEquipNewBackPack(ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount, gradeToUse, Owner.Id))
                {
                    Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.BackpackOccupied, 0, false);
                    CancelCraft();
                    return;
                }
            }
        }
        */

        // "Improper" Heuristic Grade inheritance to be used for 1.2 only, due to unset flags in compact.
        byte inheritedGrade = 0;
        Item gradeMaterial = null;
        // Find equipment materials that could provide grade  
        // Search for the first equipment material in the craft  
        foreach (var material in CurrentCraft.CraftMaterials)
        {
            var template = _itemManager.GetTemplate(material.ItemId);
            if (template is EquipItemTemplate) // Check if material is equipment  
            {
                // Search bag container for this material
                if (Owner.Inventory.Bag.GetAllItemsByTemplate(material.ItemId, -1, out var items, out _))
                {
                    if (items.Count > 0)
                    {
                        gradeMaterial = items[0];
                        inheritedGrade = gradeMaterial.Grade;
                        break;
                    }
                }
            }
        }

        var productsWithGrades = new List<(CraftProduct Product, int Grade)>();
        foreach (var product in CurrentCraft.CraftProducts)
        {
            int gradeToUse = -1;

            // If we found an equipment material, inherit grade and roll for free regrade
            if (gradeMaterial != null)
            {
                gradeToUse = FreeRegrade((int)inheritedGrade);
            }
            else if (product.ItemGradeId > 0)
            {
                gradeToUse = (int)product.ItemGradeId;
            }

            productsWithGrades.Add((product, gradeToUse));
        }

        if (!CanFitCraftProducts(productsWithGrades))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.NotEnoughSpace, 0, false);
            CancelCraft();
            return false;
        }

        // Skill.EndSkill normally settles labor after effects. CraftEffect runs inside those effects, so
        // settle it here while inventory and labor are both locked; EndSkill observes the same skill as paid.
        if (!skill.TryConsumeLabor(Owner))
        {
            Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.NotEnoughLaborPower, 0, false);
            CancelCraft();
            return false;
        }

        var productionContext = new SpecialtyPackProductionContext(
            SpecialtyPackProductionSource.Craft,
            DateTime.UtcNow,
            ProductionZoneGroupId,
            Owner.Id);

        foreach (var (product, gradeToUse) in productsWithGrades)
        {
            if (_itemManager.IsAutoEquipTradePack(product.ItemId) == false)
            {
                if (!Owner.Inventory.Bag.AcquireDefaultItem(
                        ItemTaskType.CraftActSaved,
                        product.ItemId,
                        product.Amount,
                        gradeToUse,
                        Owner.Id,
                        productionContext))
                    throw new InvalidOperationException(
                        $"Craft {CurrentCraft.Id} product {product.ItemId} failed after successful capacity preflight.");
            }
            else
            {
                if (!Owner.Inventory.TryEquipNewBackPack(
                        ItemTaskType.CraftPickupProduct,
                        product.ItemId,
                        product.Amount,
                        gradeToUse,
                        Owner.Id,
                        productionContext))
                {
                    Owner.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore, ErrorMessageType.BackpackOccupied, 0, false);
                    CancelCraft();
                    return false;
                }
            }
        }

        foreach (var (itemId, amount) in requiredMaterials)
        {
            var consumed = Owner.Inventory.Bag.ConsumeItem(
                ItemTaskType.CraftActSaved, itemId, amount, null);
            if (consumed != amount)
                throw new InvalidOperationException(
                    $"Craft {CurrentCraft.Id} material {itemId} changed during locked completion.");
        }

        Count--;
        IsCrafting = false;

        //Owner.Quests.OnCraft(_craft); // TODO added for quest Id=6024
        // инициируем событие
        //Task.Run(() =>
        //{
        //    if (_craft != null)
        //    {
        //        QuestManager.Instance.DoOnCraftEvents(Owner, _craft.Id);
        //    }
        //});
        QuestManager.Instance.DoOnCraftEvents(Owner, CurrentCraft.Id);

        if (Count > 0)
        {
            ScheduleCraft();
            // Owner.SendMessage($"Continue craft: {_craft.Id} for {_count} more times TaskId: {newCraft.Id}, cooldown: {nextCraftDelay.TotalMilliseconds}ms");
        }
        else
        {
            CancelCraft();
        }
        return true;
    }

    /// <summary>
    /// Preflights the exact product grades selected for this craft. Existing partial stacks are
    /// consumed first; only the residual quantities reserve inventory slots. This avoids rejecting
    /// stackable craft results merely because the number of product rows exceeds free slots.
    /// </summary>
    private bool CanFitCraftProducts(IEnumerable<(CraftProduct Product, int Grade)> products)
    {
        var productList = products.ToList();
        var equippedBackpack = Owner.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
        var mustStoreEquippedGlider = productList.Any(entry => _itemManager.IsAutoEquipTradePack(entry.Product.ItemId)) &&
                                       equippedBackpack?.Template is BackpackTemplate
                                       {
                                           BackpackType: BackpackType.Glider
                                       };
        var requiredSlots = mustStoreEquippedGlider ? 1 : 0;
        foreach (var group in productList
                     .Where(entry => !_itemManager.IsAutoEquipTradePack(entry.Product.ItemId))
                     .GroupBy(entry => (entry.Product.ItemId, Grade: Math.Max(entry.Grade, 0))))
        {
            var amount = group.Sum(entry => entry.Product.Amount);
            if (amount <= 0 || group.Key.ItemId == Item.Coins)
                continue;

            var template = _itemManager.GetTemplate(group.Key.ItemId);
            if (template == null || template.MaxCount <= 0)
                return false;

            Owner.Inventory.Bag.GetAllItemsByTemplate(
                group.Key.ItemId, group.Key.Grade, out var existing, out var existingAmount);
            existing = existing.Where(item => item.HasDefaultDetail && item.MadeUnitId == 0).ToList();
            existingAmount = existing.Sum(item => item.Count);
            var availableInExistingStacks = (long)existing.Count * template.MaxCount - existingAmount;
            var remainder = Math.Max(0L, amount - availableInExistingStacks);
            requiredSlots += (int)Math.Ceiling(remainder / (double)template.MaxCount);
            if (requiredSlots > Owner.Inventory.Bag.FreeSlotCount)
                return false;
        }

        return true;
    }

    private void ScheduleCraft()
    {
        var newCraft = new CraftTask(Owner, CurrentCraft.Id, DoodadId, Count);
        var skillTemplate = _skillManager.GetSkillTemplate(CurrentCraft.SkillId);
        var timeToGlobalCooldown = Owner.GlobalCooldown - DateTime.UtcNow;
        var nextCraftDelay = timeToGlobalCooldown.TotalMilliseconds > skillTemplate.CooldownTime
            ? timeToGlobalCooldown
            : TimeSpan.FromMilliseconds(skillTemplate.CooldownTime);
        TaskManager.Instance.Schedule(newCraft, nextCraftDelay);
    }

    private void CancelCraft()
    {
        IsCrafting = false;
        CurrentCraft = null;
        CurrentSkill = null;
        Count = 0;
        DoodadId = 0;
        DoodadFuncGroupId = 0;
        CraftPackId = 0;
        ProductionZoneGroupId = 0;
        ConsumeLaborPower = 0;

        // Also cancel the related skill ? I don't think this really does anything for crafts, but can't hurt I guess
        if (Owner != null)
        {
            if (Owner.SkillTask != null)
                Owner.SkillTask.Skill.Cancelled = true;
            Owner.InterruptSkills();
        }

        // Might want to send a packet here, I think there is a packet when crafting fails. Not sure yet.
    }

    private bool TryResolveCraftSource(
        Craft craft,
        uint doodadId,
        out Doodad doodad,
        out (DoodadFunc Function, DoodadFuncCraftPack Template) craftPack,
        out uint productionZoneGroupId)
    {
        doodad = null;
        craftPack = default;
        productionZoneGroupId = 0;
        var world = Owner.ParentWorld;
        if (world == null || doodadId == 0)
            return false;

        doodad = world.GetDoodad(doodadId);
        if (doodad == null || doodad.ParentWorld != world ||
            !ReferenceEquals(world.GetDoodad(doodad.ObjId), doodad) ||
            doodad.Despawn > DateTime.MinValue || doodad.FuncGroupId == 0)
            return false;
        if (!_doodadManager.TryGetActiveCraftPack(doodad, out var function, out var template) ||
            !_craftManager.IsCraftInPack(template.CraftPackId, craft.Id) ||
            !TryResolveProductionZone(craft, doodad, out productionZoneGroupId))
            return false;

        var skillTemplate = _skillManager.GetSkillTemplate(craft.SkillId);
        if (skillTemplate == null ||
            skillTemplate.MaxRange > 0 && GetDistanceTo(doodad) > skillTemplate.MaxRange)
            return false;

        craftPack = (function, template);
        return true;
    }

    protected virtual float GetDistanceTo(Doodad doodad) => Owner.GetDistanceTo(doodad, true);

    private bool TryResolveProductionZone(Craft craft, Doodad doodad, out uint productionZoneGroupId)
    {
        productionZoneGroupId = _zoneManager.GetZoneByKey(doodad.Transform.ZoneId)?.GroupId ?? 0;
        if (craft.CraftProducts.Count == 0)
            return false;

        var autoEquipProducts = 0;
        foreach (var product in craft.CraftProducts)
        {
            if (product.Amount <= 0 || _itemManager.GetTemplate(product.ItemId) is not { } itemTemplate)
                return false;
            if (_itemManager.IsAutoEquipTradePack(product.ItemId))
            {
                autoEquipProducts++;
                if (autoEquipProducts > 1 || product.Amount != 1)
                    return false;
            }
            if (itemTemplate is not BackpackTemplate { FreshnessGroupId: > 0 } freshnessTemplate)
                continue;
            if (productionZoneGroupId is 0 or > ushort.MaxValue ||
                freshnessTemplate.SpecialtyZoneId != 0 &&
                freshnessTemplate.SpecialtyZoneId != productionZoneGroupId)
                return false;
        }

        return true;
    }

    private bool TryGetRequiredBagMaterials(Craft craft, out List<(uint ItemId, int Amount)> requiredMaterials)
    {
        requiredMaterials = [];
        foreach (var group in craft.CraftMaterials.GroupBy(material => material.ItemId))
        {
            var amount = group.Sum(material => (long)material.Amount);
            if (group.Key == 0 || amount <= 0 || amount > int.MaxValue ||
                Owner.Inventory.GetItemsCount(SlotType.Inventory, group.Key) < amount)
                return false;

            requiredMaterials.Add((group.Key, (int)amount));
        }

        return true;
    }

    private bool HasCraftPermission(Doodad doodad, DoodadFuncPermission permission)
    {
        switch (permission)
        {
            case DoodadFuncPermission.Public:
            case DoodadFuncPermission.Friend:
            case DoodadFuncPermission.SiegeMaster:
            case DoodadFuncPermission.Party:
            case DoodadFuncPermission.Raid:
                return true;
            case DoodadFuncPermission.Owner:
                return doodad.OwnerId == Owner.Id;
            case DoodadFuncPermission.Account:
                if (doodad.OwnerType != DoodadOwnerType.Character)
                    return false;
                var doodadOwner = WorldManager.Instance.GetCharacterById(doodad.OwnerId);
                return doodadOwner != null && doodadOwner.AccountId == Owner.AccountId;
            case DoodadFuncPermission.HouseInZone:
                var zoneGroup = _zoneManager.GetZoneByKey(doodad.Transform.ZoneId)?.GroupId ?? 0;
                var playerHouses = new Dictionary<uint, House>();
                if (HousingManager.Instance.GetByAccountId(playerHouses, Owner.AccountId) <= 0)
                    return false;
                return playerHouses.Values.Any(playerHouse =>
                    (_zoneManager.GetZoneByKey(playerHouse.Transform.ZoneId)?.GroupId ?? 0) == zoneGroup);
            default:
                return false;
        }
    }

    /// <summary>
    ///Roll for chance of free regrade, Use when inheriting grade only.
    /// Uses a magic number for the chance based on user statistics, replace if/when actual data tables are found.
    ///</summary>
    private static int FreeRegrade(int baseGrade)
    {
        int grade = baseGrade;
        var maxGrade = ItemManager.MaxGradeValue;
        //Check grade is not already max
        if (grade != (int)maxGrade)
        {
            //5% chance
            var luckyRoll = Random.Shared.Next(0, 20);
            if (luckyRoll < 1)
            {
                grade++;
            }
        }
        return grade;
    }
}
