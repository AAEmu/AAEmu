using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemSocketing : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemSocketing;

    /// <summary>Install attempt kind on <see cref="SCItemSocketingLunagemResultPacket"/>.</summary>
    private const byte KindInstall = 1;

    /// <summary>Remove / extract kind — the tab is not seating a gem.</summary>
    private const byte KindClear = 0;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        Logger.Debug("Special effects: ItemSocketing value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (caster is not Character owner)
        {
            skill.Cancelled = true;
            Logger.Error($"Special effects: ItemSocketing caster {caster.Id} is not a character");
            return;
        }

        if (casterObj is not SkillItem gemSkillItem)
        {
            skill.Cancelled = true;
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            Logger.Error($"Special effects: ItemSocketing casterObj {casterObj} is not a SkillItem");
            return;
        }

        if (targetObj is not SkillCastItemTarget skillTargetItem)
        {
            skill.Cancelled = true;
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            Logger.Error($"Special effects: ItemSocketing targetObj {targetObj} is not a SkillCastItemTarget");
            return;
        }

        var targetItem = owner.Inventory.Bag.GetItemByItemId(skillTargetItem.Id)
            ?? owner.Inventory.Equipment.GetItemByItemId(skillTargetItem.Id);
        var gemItem = owner.Inventory.Bag.GetItemByItemId(gemSkillItem.ItemId);

        if (targetItem is null || gemItem is null)
        {
            skill.Cancelled = true;
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} or gemItem {gemSkillItem.ItemId} not found in bag/equipment");
            return;
        }

        if (targetItem is not EquipItem equipItem)
        {
            skill.Cancelled = true;
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} was not a EquipItem");
            return;
        }

        // value1 is the shipped mode: install / wipe / salvage. Branching on the caster's
        // template id treated the extract stone as a lunagem — fail-break then wiped every socket.
        switch (value1)
        {
            case ItemSocketingRules.ModeInstall:
                InstallGem(owner, equipItem, gemItem, skill, skillObject);
                return;
            case ItemSocketingRules.ModeRemove:
                RemoveAll(owner, equipItem, gemItem, skill);
                return;
            case ItemSocketingRules.ModeExtract:
                ExtractGems(owner, equipItem, gemItem, skill, skillObject);
                return;
            default:
                Logger.Warn("ItemSocketing: {0} unknown mode value1={1}", owner.Name, value1);
                owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
                skill.Cancelled = true;
                return;
        }
    }

    private void InstallGem(Character owner, EquipItem equipItem, Item gemItem, Skill skill,
        SkillObject skillObject)
    {
        using var mutation = owner.Inventory.AcquireMutation();
        void Refuse(ErrorMessageType error)
        {
            skill.Cancelled = true;
            owner.SendErrorMessage(error);
            owner.SendPacket(new SCItemSocketingLunagemResultPacket(0, equipItem.Id,
                gemItem.TemplateId, KindInstall, false));
        }
        if (!ReferenceEquals(owner.Inventory.Bag.GetItemByItemId(gemItem.Id), gemItem) ||
            !ReferenceEquals(owner.Inventory.GetItemById(equipItem.Id), equipItem) ||
            !ItemSocketingRules.IsStoneForSkill(gemItem.Template?.UseSkillId ?? 0, skill.Template.Id))
        {
            Refuse(ErrorMessageType.ItemCannotUse);
            return;
        }
        var gemCount = equipItem.GemIds.Count(id => id != 0);
        var slotTypeId = equipItem.Template switch
        {
            WeaponTemplate weapon => weapon.HoldableTemplate?.SlotTypeId ?? 0,
            ArmorTemplate armor => armor.SlotTemplate?.SlotTypeId ?? 0,
            AccessoryTemplate accessory => accessory.SlotTemplate?.SlotTypeId ?? 0,
            _ => 0u
        };
        var attempts = ItemSocketingRules.RequestedInstallAttempts(skillObject);
        var error = ItemSocketingRules.PlanInstallCosts(attempts, gemItem.Count, gemCount,
            (int)Math.Min((uint)EquipItem.SocketSlots,
                ItemManager.Instance.GetSocketNumLimit(slotTypeId, equipItem.Grade)),
            used => SocketingCost(equipItem, gemItem, used), out var costs, out var totalCost);
        if (error.HasValue)
        {
            Refuse(error.Value);
            return;
        }
        var levelLimit = ItemManager.Instance.GetSocketLevelLimit(gemItem.TemplateId);
        if (levelLimit > 0 && (equipItem.Template?.Level ?? 0) < levelLimit)
        {
            Refuse(ErrorMessageType.SocketTargetLevel);
            return;
        }
        skill.LaborUnits = attempts;
        if (!skill.CanAffordLabor(owner))
        {
            Refuse(ErrorMessageType.NotEnoughLaborPower);
            return;
        }
        if (owner.Money < totalCost)
        {
            Refuse(ErrorMessageType.NotEnoughMoney);
            return;
        }
        // Preflight covers the entire request. A genuine failed roll ends the run;
        // only the attempts actually made are paid and consumed.
        var gems = ItemSocketingRules.CompactAfterClear(equipItem.GemIds, []);
        var results = new List<byte>();
        long paid = 0;
        for (var i = 0; i < attempts; i++)
        {
            paid = checked(paid + costs[i]);
            var success = Random.Shared.Next(10000) <
                ItemManager.Instance.GetSocketChance(gemItem.TemplateId, (uint)(gemCount + i));
            results.Add(success ? (byte)1 : (byte)0);
            if (success)
                gems[gemCount + i] = gemItem.TemplateId;
            else
            {
                if (ItemManager.Instance.GetSocketFailBreaks(gemItem.TemplateId))
                    Array.Clear(gems);
                break;
            }
        }
        skill.LaborUnits = results.Count;
        if (!skill.TryConsumeLabor(owner))
        {
            Refuse(ErrorMessageType.NotEnoughLaborPower);
            return;
        }
        if (paid > 0 && !owner.SubtractMoney(SlotType.Inventory, paid, ItemTaskType.Socketing))
            throw new InvalidOperationException("Socketing money changed under the inventory mutation lease.");
        var consumed = owner.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents,
            gemItem.TemplateId, results.Count, gemItem);
        if (consumed != results.Count)
            throw new InvalidOperationException("Socketing source changed under the inventory mutation lease.");
        skill.SourceItemConsumptionHandled = true;
        equipItem.GemIds = gems;
        equipItem.IsDirty = true;
        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
        if (equipItem.SlotType == SlotType.Equipment)
            owner.UpdateGearBonuses(null, null);
        foreach (var result in results)
            owner.SendPacket(new SCItemSocketingLunagemResultPacket(result, equipItem.Id,
                gemItem.TemplateId, KindInstall, result != 0));
        Logger.Info("ItemSocketing batch: character={0} item={1} requested={2} attempted={3} gold={4} labor={5}",
            owner.Id, equipItem.Id, attempts, results.Count, paid, skill.GetLaborCost(owner));
    }
    private void RemoveAll(Character owner, EquipItem equipItem, Item stone, Skill skill)
    {
        for (var i = 0; i < equipItem.GemIds.Length; i++)
            equipItem.GemIds[i] = 0;

        Finish(owner, equipItem, stone.TemplateId, KindClear, success: true, result: 1, skill);
    }

    private void ExtractGems(Character owner, EquipItem equipItem, Item stone, Skill skill,
        SkillObject skillObject)
    {
        if (!ItemSocketingRules.IsStoneForSkill(stone.Template?.UseSkillId ?? 0, skill?.Template?.Id ?? 0))
        {
            Logger.Warn(
                "ItemSocketing extract: {0} item tpl {1} UseSkillId {2} is not stone for skill {3}",
                owner.Name, stone.TemplateId, stone.Template?.UseSkillId ?? 0, skill?.Template?.Id ?? 0);
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        if (!TryReadExtractSelection(skillObject, out var socketIndex, out var isAll))
        {
            Logger.Info("ItemSocketing extract: {0} item {1} had no extract selection",
                owner.Name, equipItem.Id);
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var slots = ItemSocketingRules.ResolveExtractSlots(equipItem.GemIds, socketIndex, isAll);
        if (slots.Count == 0)
        {
            Logger.Info(
                "ItemSocketing extract: {0} item {1} named no filled socket (index={2} isAll={3})",
                owner.Name, equipItem.Id, socketIndex, isAll);
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        // One extract stone per gem pulled — the tab's own use count for extract-all.
        if (!owner.Inventory.CheckItems(SlotType.Inventory, stone.TemplateId, slots.Count))
        {
            owner.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            skill.Cancelled = true;
            return;
        }

        // Only extractable gems return to the bag. Non-extractable ones are cleared and lost —
        // that is the tab's own warning list ("not_extractable_socket").
        var returns = new Dictionary<uint, int>();
        uint noticeType = stone.TemplateId;
        foreach (var index in slots)
        {
            var gemId = equipItem.GemIds[index];
            noticeType = gemId;
            if (!ItemManager.Instance.IsSocketGemExtractable(gemId))
                continue;
            returns[gemId] = returns.GetValueOrDefault(gemId) + 1;
        }

        var bag = owner.Inventory.Bag;
        if (!ItemSocketingRules.CanFitExtractReturns(
                returns,
                templateId => StackRoomOnly(bag, templateId),
                templateId => Math.Max(1, ItemManager.Instance.GetTemplate(templateId)?.MaxCount ?? 1),
                bag.FreeSlotCount))
        {
            owner.SendErrorMessage(ErrorMessageType.BagFull);
            skill.Cancelled = true;
            return;
        }

        var consumed = owner.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.Socketing,
            stone.TemplateId, slots.Count, stone);
        if (consumed < slots.Count)
        {
            if (consumed > 0)
                owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Socketing, stone.TemplateId, consumed);
            owner.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            skill.Cancelled = true;
            return;
        }

        var compacted = ItemSocketingRules.CompactAfterClear(equipItem.GemIds, slots);
        for (var i = 0; i < equipItem.GemIds.Length; i++)
            equipItem.GemIds[i] = compacted[i];

        foreach (var (templateId, count) in returns)
            owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Socketing, templateId, count);

        Logger.Info(
            "ItemSocketing extract: {0} cleared {1} gem(s) from item {2} with stone {3}: slots [{4}] returned={5} isAll={6}",
            owner.Name, slots.Count, equipItem.Id, stone.TemplateId, string.Join(",", slots),
            returns.Values.Sum(), isAll);

        Finish(owner, equipItem, noticeType, KindClear, success: true, result: 1, skill);
    }

    /// <summary>
    /// Type 11 is <c>index</c> + <c>isAll</c>. Older ExtraValues casts only carried the index int.
    /// </summary>
    private static bool TryReadExtractSelection(SkillObject skillObject, out int index, out bool isAll)
    {
        index = -1;
        isAll = false;
        if (skillObject is SkillObjectItemSocketExtract extract)
        {
            index = extract.Index;
            isAll = extract.IsAll;
            return true;
        }

        if (skillObject is SkillObjectExtraValues extras && extras.ReadCount > 0)
        {
            index = extras.Values[0];
            isAll = extras.ReadCount > 1 && (extras.Values[1] & 0xFF) != 0;
            return true;
        }

        return false;
    }

    private void Finish(Character owner, EquipItem equipItem, uint type, byte kind, bool success,
        byte result, Skill skill)
    {
        equipItem.IsDirty = true;

        // The piece goes out on the detail packet and nowhere else. Handing the same blob to the
        // window a second time as a task item is what leaves it drawn as broken: the window now
        // learns the cast is over from the skill's own reagent task and re-reads the piece out of
        // the bag itself, so it no longer needs to be told.
        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));

        if (equipItem.SlotType == SlotType.Equipment)
            owner.UpdateGearBonuses(null, null);

        owner.SendPacket(new SCItemSocketingLunagemResultPacket(result, equipItem.Id, type, kind, success));

        owner.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);
    }

    /// <summary>
    /// Units of <paramref name="templateId"/> that still fit into stacks already in the bag —
    /// empty slots are counted separately so two gem types cannot both claim the same free slot.
    /// </summary>
    private static int StackRoomOnly(Items.Containers.ItemContainer bag, uint templateId)
    {
        var template = ItemManager.Instance.GetTemplate(templateId);
        if (template == null || template.MaxCount <= 0)
            return 0;

        bag.GetAllItemsByTemplate(templateId, -1, out var currentItems, out var currentTotal);
        currentItems = currentItems.Where(item => item.HasDefaultDetail && item.MadeUnitId == 0).ToList();
        currentTotal = currentItems.Sum(item => item.Count);
        return Math.Max(0, currentItems.Count * template.MaxCount - currentTotal);
    }

    /// <summary>
    /// Evaluates <c>item_socketing_cost</c> (formula 38) for the socket being filled.
    /// </summary>
    private static long SocketingCost(EquipItem equipItem, Item gemItem, uint usedSockets)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ItemSocketingCost);
        if (formula == null)
            return -1;

        var parameters = new Dictionary<string, double>
        {
            { "item_level", equipItem.Template?.Level ?? 0 },
            { "socket_item_level", gemItem.Template?.Level ?? 0 },
            { "item_used_socket", usedSockets },
            { "item_socketing_cost_mul", 0 }
        };

        if (!formula.TryEvaluate(parameters, out var cost))
            return -1;
        if (!double.IsFinite(cost) || cost < 0 || cost >= long.MaxValue)
            return -1;

        return (long)cost;
    }
}
