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
            Logger.Error($"Special effects: ItemSocketing caster {caster.Id} is not a character");
            return;
        }

        if (casterObj is not SkillItem gemSkillItem)
        {
            Logger.Error($"Special effects: ItemSocketing casterObj {casterObj} is not a SkillItem");
            return;
        }

        if (targetObj is not SkillCastItemTarget skillTargetItem)
        {
            Logger.Error($"Special effects: ItemSocketing targetObj {targetObj} is not a SkillCastItemTarget");
            return;
        }

        var targetItem = owner.Inventory.Bag.GetItemByItemId(skillTargetItem.Id)
            ?? owner.Inventory.Equipment.GetItemByItemId(skillTargetItem.Id);
        var gemItem = owner.Inventory.Bag.GetItemByItemId(gemSkillItem.ItemId);

        if (targetItem is null || gemItem is null)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} or gemItem {gemSkillItem.ItemId} not found in bag/equipment");
            return;
        }

        if (targetItem is not EquipItem equipItem)
        {
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
        byte result = 0;
        var gemCount = 0u;
        foreach (var gem in equipItem.GemIds)
        {
            if (gem != 0)
                gemCount++;
        }

        // A piece only has so many sockets, and how many follows its slot and its grade. The
        // window greys its own button when the run is full, so this only catches a stale window
        // or a hand-built request.
        var slotTypeId = equipItem.Template switch
        {
            WeaponTemplate weapon => weapon.HoldableTemplate?.SlotTypeId ?? 0,
            ArmorTemplate armor => armor.SlotTemplate?.SlotTypeId ?? 0,
            AccessoryTemplate accessory => accessory.SlotTemplate?.SlotTypeId ?? 0,
            _ => 0u
        };

        if (gemCount >= ItemManager.Instance.GetSocketNumLimit(slotTypeId, equipItem.Grade))
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            return;
        }

        // item_socket_level_limits: a gem may only be seated into a piece of at least its level
        // (the socket_target_level refusal). Zero, which most gems carry, means no restriction.
        var levelLimit = ItemManager.Instance.GetSocketLevelLimit(gemItem.TemplateId);
        if (levelLimit > 0 && (equipItem.Template?.Level ?? 0) < levelLimit)
        {
            owner.SendErrorMessage(ErrorMessageType.SocketTargetLevel);
            return;
        }

        // Formula 38 prices the attempt: it climbs with the gear's level, the gem's own level and
        // how many sockets are already filled. Charged before the roll, so a player who cannot
        // cover it keeps both their coin and their gem - and a failure that clears the item is
        // still paid for, the same way a failed regrade is.
        var socketCost = SocketingCost(equipItem, gemItem, gemCount);
        if (socketCost > 0)
        {
            if (owner.Money < socketCost)
            {
                owner.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                return;
            }

            owner.SubtractMoney(SlotType.Inventory, socketCost);
        }

        var gemRoll = Random.Shared.Next(0, 10000);
        var gemChance = ItemManager.Instance.GetSocketChance(gemItem.TemplateId, gemCount);

        if (gemRoll < gemChance)
        {
            equipItem.GemIds[gemCount] = gemItem.TemplateId;
            result = 1;
        }
        else if (ItemManager.Instance.GetSocketFailBreaks(gemItem.TemplateId))
        {
            // Only the rows that say so take the rest of the piece's gems down with the attempt.
            for (var i = 0; i < equipItem.GemIds.Length; i++)
                equipItem.GemIds[i] = 0;
        }

        Finish(owner, equipItem, gemItem.TemplateId, KindInstall, result != 0, result, skill, skillObject,
            installedAttempt: true);
    }

    private void RemoveAll(Character owner, EquipItem equipItem, Item stone, Skill skill)
    {
        for (var i = 0; i < equipItem.GemIds.Length; i++)
            equipItem.GemIds[i] = 0;

        Finish(owner, equipItem, stone.TemplateId, KindClear, success: true, result: 1, skill,
            skillObject: null, installedAttempt: false);
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

        Finish(owner, equipItem, noticeType, KindClear, success: true, result: 1, skill,
            skillObject: null, installedAttempt: false);
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
        byte result, Skill skill, SkillObject skillObject, bool installedAttempt)
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

        // The tab keeps a tally of the run the player asked for and takes no further press while it
        // stands above zero. A successful result takes one off it; only an unsuccessful one clears
        // it outright. One cast seats one gem, so a press for several would leave the rest of that
        // tally hanging and the tab shut - closing the run off here hands it straight back.
        if (installedAttempt)
        {
            var requested = RequestedAttempts(skillObject);
            if (requested > 1 && result != 0)
            {
                owner.SendPacket(new SCItemSocketingLunagemResultPacket(0, equipItem.Id, type, kind, false));
            }
        }

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
    /// How many gems the tab asked to seat with one press.
    /// </summary>
    /// <remarks>
    /// It sends its spinner along in the cast's extra values - a byte for "all", then the count as
    /// an int, so a press for three arrives as 0x00000300 in the first of the thirteen. Casts logged
    /// from the tab carry one through six there and nothing else, which is the range it offers.
    /// </remarks>
    private static int RequestedAttempts(SkillObject skillObject)
    {
        if (skillObject is not SkillObjectExtraValues extras || extras.Values.Length == 0)
            return 1;

        var requested = (extras.Values[0] >> 8) & 0xFF;
        return requested < 1 ? 1 : requested;
    }

    /// <summary>
    /// Evaluates <c>item_socketing_cost</c> (formula 38) for the socket being filled.
    /// </summary>
    private static long SocketingCost(EquipItem equipItem, Item gemItem, uint usedSockets)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ItemSocketingCost);
        if (formula == null)
            return 0;

        var parameters = new Dictionary<string, double>
        {
            { "item_level", equipItem.Template?.Level ?? 0 },
            { "socket_item_level", gemItem.Template?.Level ?? 0 },
            { "item_used_socket", usedSockets },
            { "item_socketing_cost_mul", 0 }
        };

        var cost = formula.Evaluate(parameters);
        if (double.IsNaN(cost) || cost <= 0)
            return 0;

        return cost > long.MaxValue ? long.MaxValue : (long)cost;
    }
}
