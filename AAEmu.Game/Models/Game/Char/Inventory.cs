using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class Inventory
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public readonly ICharacter Owner;

    public Dictionary<SlotType, ItemContainer> _itemContainers { get; private set; }
    public ItemContainer Equipment { get; private set; }
    public ItemContainer Bag { get; private set; }
    public ItemContainer Warehouse { get; private set; }
    public ItemContainer MailAttachments { get; private set; }
    public ItemContainer SystemContainer { get; private set; }
    public ItemContainer SlaveContainer { get; private set; }
    public ItemContainer AuctionAttachments { get; private set; }
    public ulong PreviousBackPackItemId { get; set; } // used to re-equip glider when putting backpacks down

    public Inventory(ICharacter owner)
    {
        Owner = owner;
        // Create all container types
        _itemContainers = [];

        // Override Unit's created equipment container with a persistent one
        Owner.Equipment = ItemManager.Instance.GetItemContainerForCharacter(Owner.Id, SlotType.Equipment, (Character)owner, 0);

        var slotTypes = Enum.GetValues<SlotType>();
        foreach (var stv in slotTypes)
        {
            if (stv == SlotType.EquipmentMate)
                continue;

            // Take Equipment Container from Parent Unit's Equipment
            if (stv == SlotType.Equipment)
            {
                Equipment = Owner.Equipment;
                Equipment.Owner = Owner;
                _itemContainers.Add(stv, Equipment);
                continue;
            }

            //var newContainer = new ItemContainer(owner.Id, st, true, false);
            var newContainer = ItemManager.Instance.GetItemContainerForCharacter(Owner.Id, stv, (Character)owner, 0);
            newContainer.Owner = Owner; // override to be sure the object reference is set correctly
            _itemContainers.Add(stv, newContainer);
            switch (stv)
            {
                /*
                case SlotType.Equipment:
                    newContainer.ContainerSize = 28; // 28 equipment slots for 1.2 client
                    Equipment = newContainer;
                    break;
                */
                case SlotType.Inventory:
                    newContainer.ContainerSize = Owner.NumInventorySlots;
                    Bag = newContainer;
                    break;
                case SlotType.Bank:
                    newContainer.ContainerSize = Owner.NumBankSlots;
                    Warehouse = newContainer;
                    break;
                case SlotType.Mail:
                    MailAttachments = newContainer;
                    break;
                case SlotType.Auction:
                    AuctionAttachments = newContainer;
                    break;
                case SlotType.SlaveEquipment:
                    SlaveContainer = newContainer;
                    break;
                case SlotType.System:
                    SystemContainer = newContainer;
                    break;
            }
        }
    }

    #region Database

    /// <summary>
    /// Loads items into this Inventory
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="slotType"></param>
    [Obsolete("You can now use directly linked item containers, and no longer need to load them into the character object")]
    public void Load(MySqlConnection connection, SlotType? slotType = null)
    {
        // Get all items for this player
        var playeritems = ItemManager.Instance.LoadPlayerInventory(Owner);

        // Wipe inventory (don't use Wipe() here)
        foreach (var container in _itemContainers)
        {
            container.Value.Items.Clear();
            container.Value.UpdateFreeSlotCount();
        }

        // Place loaded items list in correct containers
        foreach (var item in playeritems)
        {
            if (item.SlotType != SlotType.None && _itemContainers.TryGetValue(item.SlotType, out var container))
            {
                if (!container.AddOrMoveExistingItem(ItemTaskType.Invalid, item, item.Slot))
                {
                    item._holdingContainer?.RemoveItem(ItemTaskType.Invalid, item, true);
                    Logger.Error("LoadInventory found unused item type for item, Id {0} ({1}) at {2}:{3} for {4}",
                        item.Id, item.TemplateId, item.SlotType, item.Slot,
                        Owner?.Name ?? "Id:" + item.OwnerId);
                }
            }
            else
            {
                Logger.Warn("LoadInventory found unused itemId {0} ({1}) at {2}:{3} for {4}", item.Id,
                    item.TemplateId, item.SlotType, item.Slot, Owner?.Name ?? "Id:" + item.OwnerId);
            }
        }
    }

    #endregion

    /// <summary>
    /// Sends initial player inventory and warehouse contents
    /// </summary>
    public void Send()
    {
        Owner.SendPacket(new SCCharacterInvenInitPacket(Owner.NumInventorySlots, (uint)Owner.NumBankSlots));
        SendFragmentedInventory(SlotType.Inventory, Owner.NumInventorySlots, Bag.GetSlottedItemsList().ToArray());
        SendFragmentedInventory(SlotType.Bank, (byte)Owner.NumBankSlots, Warehouse.GetSlottedItemsList().ToArray());
        SetInitialItemExpirationTimers(Owner.Equipment.Items.ToArray());
    }

    /// <summary>
    /// Consumes a item in specified container list, if the list is null, Bag -> Warehouse -> Equipment order is used.
    /// This function does not verify the total item count and will consume as much as possible
    /// It is recommended to use the ConsumeItem function of a ItemContainer itself as much as possible
    /// </summary>
    /// <param name="containersToCheck">Array of ItemContainers to check, if null, Inventory + Equipment + WaveHouse is used in that order</param>
    /// <param name="taskType"></param>
    /// <param name="templateId">Item TemplateId to consume</param>
    /// <param name="amountToConsume">Number of units to Consume</param>
    /// <param name="preferredItem">preferred Item to take units from</param>
    /// <returns></returns>
    public int ConsumeItem(SlotType[] containersToCheck, ItemTaskType taskType, uint templateId, int amountToConsume, Item preferredItem)
    {
        SlotType[] containerList;
        if ((containersToCheck != null) && (containersToCheck.Length > 0))
            containerList = containersToCheck;
        else
            containerList = [SlotType.Inventory, SlotType.Equipment, SlotType.Bank];
        var res = 0;
        foreach (var cli in containerList)
        {
            if (_itemContainers.TryGetValue(cli, out var c))
            {
                var used = c.ConsumeItem(taskType, templateId, amountToConsume, preferredItem);
                res += used;
                amountToConsume -= used;
            }
        }
        return res;
    }

    /// <summary>
    /// Checks if Inventory contains at least count amount of a item type
    /// </summary>
    /// <param name="slotType">Which container to check</param>
    /// <param name="templateId">Item Template Id</param>
    /// <param name="count">Minimum amount required</param>
    /// <returns></returns>
    public bool CheckItems(SlotType slotType, uint templateId, int count)
    {
        var totalCount = 0;
        if (_itemContainers.TryGetValue(slotType, out var c))
        {
            if (c.GetAllItemsByTemplate(templateId, -1, out _, out int itemCount))
                totalCount += itemCount;
        }
        return totalCount >= count;
    }

    /// <summary>
    /// Count the number of owned items in usable inventory space (Inventory, Equipment Slots and Warehouse)
    /// </summary>
    /// <param name="templateId">Item Template Id</param>
    /// <param name="gradeToCount">Specifies which item grade to count, counts all grades if omitted or is -1</param>
    /// <returns></returns>
    public int GetItemsCount(uint templateId, int gradeToCount = -1)
    {
        if (GetAllItemsByTemplate(null, templateId, gradeToCount, out var _, out var counted))
            return counted;
        else
            return 0;
    }

    /// <summary>
    /// Count the number of owned items in usable inventory space
    /// </summary>
    /// <param name="slotType">Which container needs to be checked</param>
    /// <param name="templateId">Item Template Id</param>
    /// <param name="gradeToCount">Specifies which item grade to count, counts all grades if omitted or is -1</param>
    /// <returns></returns>
    public int GetItemsCount(SlotType slotType, uint templateId, int gradeToCount = -1)
    {
        if (GetAllItemsByTemplate([slotType], templateId, gradeToCount, out var _, out var counted))
            return counted;
        else
            return 0;
    }

    /// <summary>
    /// Searches container for a list of items that have a specified templateId
    /// </summary>
    /// <param name="inContainerTypes">Array of SlotTypes to search in, you can leave this blank or null to check Inventory + Equipped + Warehouse</param>
    /// <param name="templateId">templateId to search for</param>
    /// <param name="foundItems">List of found item objects</param>
    /// <param name="gradeToCheck">Only list specified grade, use -1 for all grades</param>
    /// <param name="unitsOfItemFound">Total count of the count values of the found items</param>
    /// <returns>True if any item was found</returns>
    public bool GetAllItemsByTemplate(SlotType[] inContainerTypes, uint templateId, int gradeToCheck, out List<Item> foundItems, out int unitsOfItemFound)
    {
        bool res = false;
        foundItems = [];
        unitsOfItemFound = 0;
        if (inContainerTypes == null || inContainerTypes.Length <= 0)
        {
            inContainerTypes = [SlotType.Inventory, SlotType.Equipment, SlotType.Bank];
        }
        foreach (var ct in inContainerTypes)
        {
            if (_itemContainers.TryGetValue(ct, out var c))
            {
                res |= c.GetAllItemsByTemplate(templateId, gradeToCheck, out var theseItems, out var theseAmounts);
                foundItems.AddRange(theseItems);
                unitsOfItemFound += theseAmounts;
            }
        }
        return res;
    }

    /// <summary>
    /// Internally used by SplitOrMoveItem()
    /// </summary>
    private enum SwapAction
    {
        doNothing,
        doSwap,
        doSplit,
        doMerge,
        doMoveAllToEmpty,
        doEquipInEmptySlot,
    }

    /// <summary>
    /// All-purpose function to move items from one slot to another
    /// </summary>
    /// <param name="taskType">Task type to report to the player</param>
    /// <param name="fromItemId">Source Item TemplateId</param>
    /// <param name="fromType">Source container</param>
    /// <param name="fromSlot">Source Slot Number</param>
    /// <param name="toItemId">Item TemplateId of the item in the target slot</param>
    /// <param name="toType">Target Container</param>
    /// <param name="toSlot">Target Slot Number</param>
    /// <param name="count">Amount of units to move or split from the source item or all in the slot if omitted or 0</param>
    /// <returns>False if action failed</returns>
    public bool SplitOrMoveItem(ItemTaskType taskType, ulong fromItemId, SlotType fromType, byte fromSlot,
        ulong toItemId, SlotType toType, byte toSlot, int count = 0)
    {
        var fromItem = ItemManager.Instance.GetItemByItemId(fromItemId);
        if (fromItem == null && fromItemId != 0)
        {
            Logger.Error($"SplitOrMoveItem - ItemId {fromItemId} no longer exists, possibly a phantom item.");
            return false;
        }

        var toItem = ItemManager.Instance.GetItemByItemId(toItemId);
        if (toItem == null && toItemId != 0)
        {
            Logger.Error($"SplitOrMoveItem - ItemId {toItemId} no longer exists, possibly a phantom item.");
            return false;
        }

        // Grab containers for easy manipulation
        var sourceContainer = fromItem?._holdingContainer ?? _itemContainers.GetValueOrDefault(fromType);
        var targetContainer = toItem?._holdingContainer ?? _itemContainers.GetValueOrDefault(toType);

        return SplitOrMoveItemEx(taskType, sourceContainer, targetContainer, fromItemId, fromType, fromSlot, toItemId, toType, toSlot, count);
    }

    public bool SplitOrMoveItemEx(ItemTaskType taskType, ItemContainer sourceContainer, ItemContainer targetContainer, ulong fromItemId, SlotType fromType, byte fromSlot, ulong toItemId, SlotType toType, byte toSlot, int count = 0)
    {
        Logger.Trace($"SplitOrMoveItem({fromItemId} {fromType}:{fromSlot} => {toItemId} {toType}:{toSlot} - {count})");

        // Try to grab the actual source item
        var fromItem = ItemManager.Instance.GetItemByItemId(fromItemId);
        if (fromItem == null && fromItemId != 0)
        {
            Logger.Error($"SplitOrMoveItem - ItemId {fromItemId} no longer exists, possibly a phantom item.");
            return false;
        }

        var action = SwapAction.doNothing;

        // Try to grab the target item by its itemId
        var itemInTargetSlot = ItemManager.Instance.GetItemByItemId(toItemId);
        // If no count provided, and we have a source item, use that item's total count instead
        if (count <= 0 && fromItem != null)
            count = fromItem.Count;

        // If target item is not provided (itemId was not found), grab the item in the target slot instead
        if (itemInTargetSlot == null)
            itemInTargetSlot = targetContainer.GetItemBySlot(toSlot);

        // Check if containers can accept the items
        if (targetContainer is not null && !targetContainer.CanAccept(fromItem, toSlot))
        {
            Logger.Error($"SplitOrMoveItem - fromItemId {fromItemId} is not welcome in this container {targetContainer.ContainerType} ({targetContainer.ContainerId}).");
            return false;
        }
        if (sourceContainer is not null && !sourceContainer.CanAccept(itemInTargetSlot, fromSlot))
        {
            Logger.Error($"SplitOrMoveItem - toItemId {toItemId} is not welcome in this container {sourceContainer.ContainerType} ({sourceContainer.ContainerId}).");
            return false;
        }

        // Are we equipping into an empty slot ? For whatever reason the client will send FROM empty equipment slot => TO item to equip
        if (fromItemId == 0 && fromType == SlotType.Equipment && toType != SlotType.Equipment && itemInTargetSlot != null)
        {
            action = SwapAction.doEquipInEmptySlot;
            // sourceContainer = Equipment;
        }

        // Are we equipping it into an empty mount gear slot?
        if (fromItemId == 0 && fromType == SlotType.EquipmentMate && toType != SlotType.EquipmentMate && itemInTargetSlot != null)
        {
            action = SwapAction.doEquipInEmptySlot;
            // In case of MateEquipment the source container is always sent as the pet's container,
            // and targetContainer is always the player inventory. It does not need to be overriden.
            // sourceContainer = Equipment;
        }

        // Check some conditions when we are not equipping into an empty slot
        if (action != SwapAction.doEquipInEmptySlot && fromItem == null)
        {
            Logger.Error("SplitOrMoveItem didn't provide a source itemId");
            return false;
        }

        // Check if what the client provides as its source container is actually what the server has as data for the item
        if (action != SwapAction.doEquipInEmptySlot && sourceContainer?.ContainerType != fromType)
        {
            Logger.Error("SplitOrMoveItem Source Item Container did not match what the client asked");
            return false;
        }

        // Check if what the client provides as its source slot number is actually what the server has as data for the item
        if (action != SwapAction.doEquipInEmptySlot && fromItem?.Slot != fromSlot)
        {
            Logger.Error("SplitOrMoveItem Source Item slot did not match what the client asked");
            return false;
        }

        // Check if the amount we want to move is actually available from source
        if (action != SwapAction.doEquipInEmptySlot && count > fromItem?.Count)
        {
            Logger.Error("SplitOrMoveItem Source Item has less item count than is requested to be moved");
            return false;
        }

        // Validate target Item stuff
        if (itemInTargetSlot != null)
        {
            // Check if target slot type is what the server has on data
            if (itemInTargetSlot.SlotType != toType)
            {
                Logger.Error("SplitOrMoveItem Target Item Type does not match");
                return false;
            }

            // Check if target slot number is what the server has on data
            if (itemInTargetSlot.Slot != toSlot)
            {
                Logger.Error("SplitOrMoveItem Target Item Slot does not match");
                return false;
            }

            // Check if target slot has enough room left for this item
            if (action != SwapAction.doEquipInEmptySlot && itemInTargetSlot.TemplateId == fromItem?.TemplateId && itemInTargetSlot.Count + count > fromItem.Template.MaxCount && fromItem.Template.MaxCount > 1)
            {
                Logger.Error("SplitOrMoveItem Target Item stack does not have enough room to take source");
                return false;
            }
        }

        // Decide what type of thing we need to do
        if (action != SwapAction.doEquipInEmptySlot)
        {
            if (itemInTargetSlot == null && fromItem?.Count > count)
                action = SwapAction.doSplit;
            else if (itemInTargetSlot == null && fromItem?.Count == count)
                action = SwapAction.doMoveAllToEmpty;
            else if (itemInTargetSlot != null && itemInTargetSlot.TemplateId == fromItem?.TemplateId && itemInTargetSlot.Template.MaxCount > 1)
                action = SwapAction.doMerge;
            else
                action = SwapAction.doSwap;
        }

        var doUnEquipOffhand = false;
        var doUnEquipMainHand = false;
        Item mainHandWeapon = null;
        Item offHandWeapon = null;

        // If swapping items or equipping new items
        if (action == SwapAction.doSwap || action == SwapAction.doEquipInEmptySlot)
        {
            // Grab reference to old weapon slots
            mainHandWeapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            offHandWeapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);

            // Check for equipping weapons by swapping (and if it's a 2-handed one)
            var isFrom2H = false;
            if (fromItem != null && fromItem.Template is WeaponTemplate weaponFrom)
            {
                switch ((EquipmentItemSlotType)weaponFrom.HoldableTemplate.SlotTypeId)
                {
                    case EquipmentItemSlotType.TwoHanded:
                        isFrom2H = true;
                        break;
                    case EquipmentItemSlotType.Mainhand:
                    case EquipmentItemSlotType.Offhand:
                    case EquipmentItemSlotType.Shield:
                    case EquipmentItemSlotType.OneHanded:
                        //isFromNon2HWeapon = true;
                        break;
                    default:
                        break;
                }
            }

            var isTo2H = false;
            if (itemInTargetSlot != null && itemInTargetSlot.Template is WeaponTemplate weaponTo)
            {
                switch ((EquipmentItemSlotType)weaponTo.HoldableTemplate.SlotTypeId)
                {
                    case EquipmentItemSlotType.TwoHanded:
                        isTo2H = true;
                        break;
                    case EquipmentItemSlotType.Mainhand:
                    case EquipmentItemSlotType.Offhand:
                    case EquipmentItemSlotType.Shield:
                    case EquipmentItemSlotType.OneHanded:
                        //isToNon2HWeapon = true;
                        break;
                    default:
                        break;
                }
            }

            var isMain2H = false;
            if (mainHandWeapon != null && mainHandWeapon.Template is WeaponTemplate mainWeapon)
            {
                switch ((EquipmentItemSlotType)mainWeapon.HoldableTemplate.SlotTypeId)
                {
                    case EquipmentItemSlotType.TwoHanded:
                        isMain2H = true;
                        break;
                    case EquipmentItemSlotType.Mainhand:
                    case EquipmentItemSlotType.Offhand:
                    case EquipmentItemSlotType.Shield:
                    case EquipmentItemSlotType.OneHanded:
                        // isFromNon2HWeapon = true;
                        break;
                    default:
                        break;
                }
            }

            // If equipping a 2-Handed weapon, and we had an off-hand slot weapon equipped, mark it for un-equip 
            if (isTo2H && sourceContainer?.ContainerType == SlotType.Equipment && fromSlot == (int)EquipmentItemSlot.Mainhand)
                doUnEquipOffhand = true;

            // If equipping a 2-Handed weapon, and we had a main slot weapon equipped, mark it for un-equip
            if (isMain2H && sourceContainer?.ContainerType == SlotType.Equipment && fromSlot == (int)EquipmentItemSlot.Offhand)
                doUnEquipMainHand = true;

            // Client actually always sends from equipment => inventory no matter how you click it, this is just a safety if it ever changes
            // Same checks as above, but in reverse order
            if (isFrom2H && targetContainer?.ContainerType == SlotType.Equipment && toSlot == (int)EquipmentItemSlot.Mainhand)
                doUnEquipOffhand = true;
            if (isMain2H && targetContainer?.ContainerType == SlotType.Equipment && toSlot == (int)EquipmentItemSlot.Offhand)
                doUnEquipMainHand = true;
        }

        // If we need to un-equip the off-hand weapon because of equipping a 2-handed weapon 
        if (doUnEquipOffhand && offHandWeapon != null)
        {
            // Check if we have enough space to un-equip the offhand
            if (Bag.FreeSlotCount < 1)
                return false;
            // If we can't move it to bag for whatever reason, abort
            if (!Bag.AddOrMoveExistingItem(taskType, offHandWeapon))
                return false;
        }

        // If we need to un-equip the main-hand weapon because of equipping a 2-handed weapon
        if (doUnEquipMainHand)
        {
            // Check if we have enough space to un-equip the main hand weapon
            if (Bag.FreeSlotCount < 1)
                return false;
            // If we can't move it to bag for whatever reason, abort
            if (!Bag.AddOrMoveExistingItem(taskType, mainHandWeapon))
                return false;
        }

        // Owner.SendMessage($"ItemMove {action}: {fromItemId} {fromType}:{fromSlot} => {toItemId} {toType}:{toSlot} - {count}");
        // Actually execute what we need to do
        var itemTasks = new List<ItemTask>();
        switch (action)
        {
            case SwapAction.doEquipInEmptySlot:
                itemInTargetSlot.SlotType = sourceContainer.ContainerType;
                itemInTargetSlot.Slot = fromSlot;

                itemTasks.Add(new ItemMove(fromType, fromSlot, fromItemId, toType, toSlot, toItemId));
                if (targetContainer != sourceContainer)
                {
                    sourceContainer.Items.Add(itemInTargetSlot);
                    targetContainer.Items.Remove(itemInTargetSlot);
                    itemInTargetSlot._holdingContainer = sourceContainer;
                    sourceContainer.UpdateFreeSlotCount();
                    targetContainer.UpdateFreeSlotCount();
                }
                break;
            case SwapAction.doSplit:
                fromItem.Count -= count;
                itemTasks.Add(new ItemCountUpdate(fromItem, -count));
                var ni = ItemManager.Instance.Create(fromItem.TemplateId, count, fromItem.Grade, true);
                ni.SlotType = toType;
                ni.Slot = toSlot;
                ni._holdingContainer = targetContainer;
                targetContainer.Items.Add(ni);
                itemTasks.Add(new ItemAdd(ni));
                if (targetContainer != sourceContainer)
                    targetContainer.UpdateFreeSlotCount();
                sourceContainer.UpdateFreeSlotCount();
                break;
            case SwapAction.doMoveAllToEmpty:
                fromItem.SlotType = targetContainer.ContainerType;
                fromItem.Slot = toSlot;
                itemTasks.Add(new ItemMove(fromType, fromSlot, fromItemId, toType, toSlot, toItemId));
                // itemTasks.Add(new ItemMove(fromType, fromSlot, fromItem.Id, toType, toSlot, toItemId));
                if (targetContainer != sourceContainer)
                {
                    sourceContainer.Items.Remove(fromItem);
                    targetContainer.Items.Add(fromItem);
                    fromItem._holdingContainer = targetContainer;
                    sourceContainer.UpdateFreeSlotCount();
                    targetContainer.UpdateFreeSlotCount();
                }
                break;
            case SwapAction.doMerge:
                // Merge x amount into target
                var toAddCount = Math.Min(count, itemInTargetSlot!.Template.MaxCount - itemInTargetSlot.Count);
                if (toAddCount < count)
                    Logger.Trace($"SplitOrMoveItem supplied more than target can take, changed {count} to {toAddCount}");
                itemInTargetSlot.Count += toAddCount;
                fromItem.Count -= toAddCount;
                if (fromItem.Count > 0)
                {
                    itemTasks.Add(new ItemCountUpdate(fromItem, -toAddCount));
                }
                else
                {
                    itemTasks.Add(new ItemRemove(fromItem));
                    sourceContainer.RemoveItem(ItemTaskType.Invalid, fromItem, true);
                }
                itemTasks.Add(new ItemCountUpdate(itemInTargetSlot, toAddCount));
                break;
            case SwapAction.doSwap:
                // Swap both item slots
                itemTasks.Add(new ItemMove(fromType, fromSlot, fromItemId, toType, toSlot, toItemId));
                // itemTasks.Add(new ItemMove(fromType, fromSlot, fromItem!.Id, toType, toSlot, itemInTargetSlot!.Id));
                fromItem!.SlotType = targetContainer!.ContainerType;
                fromItem.Slot = toSlot;
                if (sourceContainer != null && sourceContainer != targetContainer)
                {
                    sourceContainer.Items.Remove(fromItem);
                    targetContainer.Items.Add(fromItem);
                    fromItem._holdingContainer = targetContainer;
                    targetContainer.UpdateFreeSlotCount();
                }
                itemInTargetSlot!.SlotType = sourceContainer!.ContainerType;
                itemInTargetSlot.Slot = fromSlot;
                if (sourceContainer != targetContainer)
                {
                    targetContainer.Items.Remove(itemInTargetSlot);
                    sourceContainer.Items.Add(itemInTargetSlot);
                    itemInTargetSlot._holdingContainer = sourceContainer;
                    sourceContainer.UpdateFreeSlotCount();
                }
                break;
            case SwapAction.doNothing:
            default:
                // Should be impossible to get here
                Owner.SendMessage("|cFFFF0000SplitOrMoveItem swap action not implemented " + action + "|r");
                Logger.Error("SplitOrMoveItem swap action not implemented " + action);
                break;
        }

        // Force-assign item owners for safety
        if (fromItem != null)
            fromItem.OwnerId = sourceContainer?.OwnerId ?? 0;
        if (itemInTargetSlot != null)
            itemInTargetSlot.OwnerId = targetContainer?.OwnerId ?? 0;

        // Send Item manipulation packet 
        if (taskType != ItemTaskType.Invalid && itemTasks.Count > 0)
            Owner.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, []));

        // Send ItemContainer events
        if (sourceContainer != targetContainer)
        {
            if (fromItem != null)
            {
                sourceContainer?.OnLeaveContainer(fromItem, targetContainer, fromSlot);
                targetContainer?.OnEnterContainer(fromItem, sourceContainer, fromSlot);
            }

            if (itemInTargetSlot != null)
            {
                targetContainer?.OnLeaveContainer(itemInTargetSlot, sourceContainer, fromSlot);
                sourceContainer?.OnEnterContainer(itemInTargetSlot, targetContainer, fromSlot);
            }
        }

        sourceContainer.ApplyBindRules(taskType);
        if (targetContainer != sourceContainer)
            targetContainer.ApplyBindRules(taskType);

        return itemTasks.Count > 0;
    }

    /// <summary>
    /// Check if player should be able to un-equip their currently equipped glider
    /// </summary>
    /// <returns>Returns true if it should be possible, or if nothing is equipped in the backpack slot</returns>
    public bool CanReplaceGliderInBackpackSlot()
    {
        var backpack = GetEquippedBySlot(EquipmentItemSlot.Backpack);
        if (backpack == null)
            return true; // Nothing equipped, so we're good

        // Check if a glider is equipped, and if we have at least 1 free space
        if (backpack.Template is BackpackTemplate bt && bt.BackpackType == BackpackType.Glider && Bag.FreeSlotCount > 0)
            return true;

        // Something other than a glider is equipped in the backpack slot, don't allow replacing check
        return false;
    }

    /// <summary>
    /// Moves equipped backpack into the inventory
    /// </summary>
    /// <param name="taskType"></param>
    /// <param name="glidersOnly">When true, only allow for gliders</param>
    /// <returns></returns>
    public bool TakeoffBackpack(ItemTaskType taskType, bool glidersOnly = false)
    {
        var backpack = GetEquippedBySlot(EquipmentItemSlot.Backpack);
        if (backpack == null) return true;

        // Check glider if needed
        if (glidersOnly && backpack.Template is BackpackTemplate bt && bt.BackpackType != BackpackType.Glider)
            return false;

        // Move to first available slot
        if (Bag.FreeSlotCount <= 0)
            return false;

        if (!SplitOrMoveItem(taskType, backpack.Id, backpack.SlotType, (byte)backpack.Slot, 0, SlotType.Inventory, (byte)Bag.GetUnusedSlot(-1)))
            return false;

        if (glidersOnly)
            PreviousBackPackItemId = backpack.Id;

        return true;
    }

    /// <summary>
    /// Try to create a new backpack item and immediately equip it into the backpack slot (used for crafting tradepacks)
    /// </summary>
    /// <param name="taskType"></param>
    /// <param name="itemId">Item TemplateId</param>
    /// <param name="itemCount"></param>
    /// <param name="gradeToAdd"></param>
    /// <param name="crafterId"></param>
    /// <returns></returns>
    public bool TryEquipNewBackPack(ItemTaskType taskType, uint itemId, int itemCount, int gradeToAdd = -1, uint crafterId = 0)
    {
        // Remove player backpack
        if (Owner.Inventory.TakeoffBackpack(taskType, true))
        {
            // Put tradepack in their backpack slot
            return Owner.Inventory.Equipment.AcquireDefaultItem(taskType, itemId, itemCount, gradeToAdd, crafterId);
        }
        return false;
    }

    /// <summary>
    /// Tries to add a new item to the player's inventory bag (or backpack slot if it's a auto-equip backpack)
    /// </summary>
    /// <param name="taskType"></param>
    /// <param name="itemId">Item Template Id</param>
    /// <param name="itemCount"></param>
    /// <param name="gradeToAdd"></param>
    /// <param name="crafterId"></param>
    /// <returns></returns>
    public bool TryAddNewItem(ItemTaskType taskType, uint itemId, int itemCount, int gradeToAdd = -1, uint crafterId = 0)
    {
        if (ItemManager.Instance.IsAutoEquipTradePack(itemId))
            return TryEquipNewBackPack(taskType, itemId, itemCount, gradeToAdd, crafterId);

        return Bag.AcquireDefaultItem(taskType, itemId, itemCount, gradeToAdd, crafterId);
    }

    /// <summary>
    /// Get Item from it's ItemId
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Item GetItemById(ulong id)
    {
        foreach (var c in _itemContainers)
        {
            if (c.Key == SlotType.Equipment || c.Key == SlotType.Inventory || c.Key == SlotType.Bank)
            {
                foreach (var i in c.Value.Items)
                {
                    if (i != null && i.Id == id)
                        return i;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Get Item from specified equipment slot
    /// </summary>
    /// <param name="slot"></param>
    /// <returns></returns>
    public Item GetEquippedBySlot(EquipmentItemSlot slot)
    {
        return Equipment.GetItemBySlot((byte)slot);
    }

    /// <summary>
    /// Get Item from specified container and slot
    /// </summary>
    /// <param name="type"></param>
    /// <param name="slot"></param>
    /// <returns></returns>
    public Item GetItem(SlotType type, byte slot)
    {
        Item item = null;
        switch (type)
        {
            case SlotType.None:
                // TODO ...
                break;
            case SlotType.Equipment:
                item = Equipment.GetItemBySlot(slot);
                break;
            case SlotType.Inventory:
                item = Bag.GetItemBySlot(slot);
                break;
            case SlotType.Bank:
                item = Warehouse.GetItemBySlot(slot);
                break;
            case SlotType.SlaveEquipment:
                item = Bag.GetItemBySlot(slot);
                break;
            case SlotType.Trade:
                // TODO ...
                break;
            case SlotType.Mail:
                // TODO ...
                break;
        }

        return item;
    }

    /// <summary>
    /// Get the number of free slots for target container
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public int FreeSlotCount(SlotType type)
    {
        if (_itemContainers.TryGetValue(type, out var c))
            return c.FreeSlotCount;
        else
            return 0;
    }

    private void SetInitialItemExpirationTimers(Item[] bag)
    {
        // Send Item Expire Times if needed
        foreach (var item in bag)
        {
            if (item?.ExpirationTime > DateTime.MinValue)
                Owner.SendPacket(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, item.ExpirationTime));
            if (item?.ExpirationOnlineMinutesLeft > 0.0)
                Owner.SendPacket(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, DateTime.UtcNow.AddMinutes(item.ExpirationOnlineMinutesLeft)));
        }
    }

    private void SendFragmentedInventory(SlotType slotType, byte numItems, Item[] bag)
    {
        if (bag.Length % 10 != 0)
            Logger.Warn($"SendFragmentedInventory: Inventory Size not a multiple of 10 ({numItems})");
        if (bag.Length != numItems)
            Logger.Warn($"SendFragmentedInventory: Inventory Size Mismatch; expected {numItems} got {bag.Length}");

        /*
         * указывает, сколько предметов должно быть в пакете
         * indicates how many items should be in the package
         * numChunks = 1 -> 10 for 1.2
         * numChunks = 5 -> 50 for 3+
         */
        const int NumChunks = 1;
        var startChunkIdx = 0;
        var dividedArrays = Helpers.SplitArray(bag, NumChunks * 10);
        foreach (var item in dividedArrays)
        {
            startChunkIdx *= NumChunks;
            Owner.SendPacket(new SCCharacterInvenContentsPacket(slotType, NumChunks, (byte)startChunkIdx, item));
            startChunkIdx++;
        }

        SetInitialItemExpirationTimers(bag);
    }

    /// <summary>
    /// Try to increases the amount of total slots for specified container
    /// </summary>
    /// <param name="slotType"></param>
    public void ExpandSlot(SlotType slotType)
    {
        var isBank = slotType == SlotType.Bank;
        var step = ((isBank ? Owner.NumBankSlots : Owner.NumInventorySlots) - 50) / 10;
        var expands = CharacterManager.Instance.GetExpands(step);
        if (expands == null)
            return;
        var index = expands.FindIndex(e => e.IsBank == isBank);
        if (index == -1)
            return;
        var expand = expands[index];
        if (expand.Price != 0 && Owner.Money < expand.Price)
        {
            Logger.Warn("No Money for expand!");
            return;
        }

        if (expand.ItemId != 0 && expand.ItemCount != 0 && !CheckItems(SlotType.Inventory, expand.ItemId, expand.ItemCount))
        {
            Logger.Warn("Item or Count not fount.");
            return;
        }

        var tasks = new List<ItemTask>();
        if (expand.Price != 0)
        {
            Owner.Money -= expand.Price;
            tasks.Add(new MoneyChange(-expand.Price));
        }

        if (expand.ItemId != 0 && expand.ItemCount != 0)
        {
            Bag.ConsumeItem(isBank ? ItemTaskType.ExpandBank : ItemTaskType.ExpandBag, expand.ItemId, expand.ItemCount, null);
        }

        if (isBank)
        {
            Owner.NumBankSlots = (short)(50 + 10 * (1 + step));
            Warehouse.ContainerSize = Owner.NumBankSlots;
        }
        else
        {
            Owner.NumInventorySlots = (byte)(50 + 10 * (1 + step));
            Bag.ContainerSize = Owner.NumInventorySlots;
        }

        Owner.SendPacket(
            new SCInvenExpandedPacket(
                isBank ? SlotType.Bank : SlotType.Inventory,
                isBank ? (byte)Owner.NumBankSlots : Owner.NumInventorySlots
            )
        );
    }

    /// <summary>
    /// Triggers whenever a new item is added to the player
    /// </summary>
    /// <param name="item"></param>
    /// <param name="count"></param>
    /// <param name="onlyUpdatedCount"></param>
    public void OnAcquiredItem(Item item, int count, bool onlyUpdatedCount = false)
    {
        // Quests
        //if ((item?.Template.LootQuestId > 0) && (count != 0))
        if (count > 0 && item != null)
        {
            //Owner?.Quests?.OnItemGather(item, count);
            // инициируем событие
            //Task.Run(() => QuestManager.Instance.DoAcquiredEvents((Character)Owner, item.TemplateId, item.Count));
            QuestManager.Instance.DoItemsAcquiredEvents(Owner, item.TemplateId, item.Count);
        }
    }

    /// <summary>
    /// Triggers whenever an item (count) is removed
    /// </summary>
    /// <param name="item"></param>
    /// <param name="count"></param>
    /// <param name="onlyUpdatedCount"></param>
    public void OnConsumedItem(Item item, int count, bool onlyUpdatedCount = false)
    {
        // Quests
        if (item != null)
        {
            QuestManager.Instance.DoItemsConsumedEvents(Owner, item.TemplateId, count);
        }
    }

    public void OnItemManuallyDestroyed(Item item, int count)
    {
        item?.OnManuallyDestroyingItem();
        if (item?.Template.LootQuestId > 0)
            if (Owner.Quests.HasQuest(item.Template.LootQuestId))
                Owner.Quests.OnQuestItemManuallyDestroyed(item);
    }

    public bool SwapCofferItems(ulong fromItemId, ulong toItemId, SlotType fromSlotType, byte fromSlot, SlotType toSlotType, byte toSlot, ulong dbId)
    {
        // TODO: Verify if you have access to the coffer

        var relatedCoffer = ItemManager.Instance.GetItemContainerByDbId(dbId);

        ItemContainer sourceContainer = null;
        ItemContainer targetContainer = null;

        if (fromSlotType == SlotType.Trade)
            sourceContainer = relatedCoffer;
        else if (_itemContainers.TryGetValue(fromSlotType, out var sC))
            sourceContainer = sC;

        if (toSlotType == SlotType.Trade)
            targetContainer = relatedCoffer;
        else if (_itemContainers.TryGetValue(toSlotType, out var tC))
            targetContainer = tC;

        if (sourceContainer == null || targetContainer == null)
        {
            Logger.Error("SwapCofferItems, not all of the targetted containers exist");
            return false;
        }

        return SplitOrMoveItemEx(ItemTaskType.SwapCofferItems, sourceContainer, targetContainer, fromItemId, fromSlotType, fromSlot,
            toItemId, toSlotType, toSlot);
    }

    public bool SplitCofferItems(int count, ulong fromItemId, ulong toItemId, SlotType fromSlotType, byte fromSlot, SlotType toSlotType, byte toSlot, ulong dbId)
    {
        // TODO: Verify if you have access to the coffer

        var relatedCoffer = ItemManager.Instance.GetItemContainerByDbId(dbId);

        ItemContainer sourceContainer = null;
        ItemContainer targetContainer = null;

        if (fromSlotType == SlotType.Trade)
            sourceContainer = relatedCoffer;
        else if (_itemContainers.TryGetValue(fromSlotType, out var sC))
            sourceContainer = sC;

        if (toSlotType == SlotType.Trade)
            targetContainer = relatedCoffer;
        else if (_itemContainers.TryGetValue(toSlotType, out var tC))
            targetContainer = tC;

        if (sourceContainer == null || targetContainer == null)
        {
            Logger.Error("SwapCofferItems, not all of the targetted containers exist");
            return false;
        }

        return SplitOrMoveItemEx(ItemTaskType.SplitCofferItems, sourceContainer, targetContainer, fromItemId, fromSlotType, fromSlot,
            toItemId, toSlotType, toSlot, count);
    }
}
