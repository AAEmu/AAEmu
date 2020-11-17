using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Utils.DB;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MySql.Data.MySqlClient;
using NLog;
using NLog.Targets;

namespace AAEmu.Game.Models.Game.Char
{

    public class Inventory
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public readonly Character Owner;

        public Dictionary<SlotType, ItemContainer> _itemContainers { get; private set; }
        public ItemContainer Equipment { get; private set; }
        public ItemContainer Bag { get; private set; }
        public ItemContainer Warehouse { get; private set; }
        public ItemContainer MailAttachments { get; private set; }

        public Inventory(Character owner)
        {
            Owner = owner;
            // Create all container types
            _itemContainers = new Dictionary<SlotType, ItemContainer>();

            var SlotTypes = Enum.GetValues(typeof(SlotType));
            foreach (var stv in SlotTypes)
            {
                SlotType st = (SlotType)stv;
                // Take Equipment Container from Parent Unit's Equipment
                if (st == SlotType.Equipment)
                {
                    Equipment = Owner.Equipment;
                    Equipment.Owner = Owner;
                    Equipment.PartOfPlayerInventory = true;
                    _itemContainers.Add(st,Equipment);
                    continue;
                }
                var newContainer = new ItemContainer(owner, st, true);
                _itemContainers.Add(st, newContainer);
                switch (st)
                {
                    case SlotType.Equipment:
                        newContainer.ContainerSize = 28; // 28 equipment slots for 1.2 client
                        Equipment = newContainer;
                        break;
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
                }
            }

        }

        #region Database

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
                if ((item.SlotType != SlotType.None) && (_itemContainers.TryGetValue(item.SlotType, out var container)))
                {
                    if (!container.AddOrMoveExistingItem(ItemTaskType.Invalid, item, item.Slot))
                    {
                        item._holdingContainer?.RemoveItem(ItemTaskType.Invalid, item, true);
                        _log.Error("LoadInventory found unused item type for item, Id {0} ({1}) at {2}:{3} for {1}", item.Id, item.TemplateId, item.SlotType, item.Slot, Owner?.Name ?? "Id:"+item.OwnerId.ToString());
                        // throw new Exception(string.Format("Was unable to add item {0} to container {1} for player {2} using the defined slot.", item?.Template.Name ?? item.TemplateId.ToString(), item.Slot.ToString(), Owner?.Name ?? "???"));
                    }
                }
                else
                {
                    _log.Warn("LoadInventory found unused itemId {0} ({1}) at {2}:{3} for {1}", item.Id, item.TemplateId, item.SlotType, item.Slot, Owner?.Name ?? "Id:" + item.OwnerId.ToString());
                }
            }

        }


        [Obsolete("Items are no longer saves individual player items. It is instead handled by the ItemManager instead, this function does nothing")]
        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            // Nothing
        }
        #endregion

        public void Send()
        {
            Owner.SendPacket(new SCCharacterInvenInitPacket(Owner.NumInventorySlots, (uint)Owner.NumBankSlots));
            SendFragmentedInventory(SlotType.Inventory, Owner.NumInventorySlots, Bag.GetSlottedItemsList().ToArray());
            SendFragmentedInventory(SlotType.Bank, (byte)Owner.NumBankSlots, Warehouse.GetSlottedItemsList().ToArray());
        }

        public Item AddItem(ItemTaskType taskType, Item item)
        {
            if (!_itemContainers.TryGetValue(item.SlotType, out var targetContainer))
                throw new Exception(string.Format("Inventory.AddItem(); Item has a slottype that has no supported container"));

            if (targetContainer.AddOrMoveExistingItem(taskType, item, item.Slot))
                return item;
            else
                return null;
        }

        public bool RemoveItem(ItemTaskType taskType, Item item, bool release)
        {
            bool res = false;
            foreach (var c in _itemContainers)
                res |= c.Value.RemoveItem(taskType, item, release);
            return res;
        }

        /// <summary>
        /// Consumes a item in specified container list, if the list is null, Bag -> Warehouse -> Equipment order is used. This function does not verify the total item count and will consume as much as possible
        /// </summary>
        /// <param name="containersToCheck"></param>
        /// <param name="taskType"></param>
        /// <param name="templateId"></param>
        /// <param name="amountToConsume"></param>
        /// <param name="preferredItem"></param>
        /// <returns></returns>
        public int ConsumeItem(SlotType[] containersToCheck, ItemTaskType taskType, uint templateId, int amountToConsume, Item preferredItem)
        {
            SlotType[] containerList;
            if (containersToCheck != null)
                containerList = containersToCheck;
            else
                containerList = new SlotType[3] { SlotType.Inventory, SlotType.Bank, SlotType.Equipment };
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

        public bool CheckItems(SlotType slotType, uint templateId, int count)
        {
            var totalCount = 0;
            if (_itemContainers.TryGetValue(slotType, out var c))
            {
                if (c.GetAllItemsByTemplate(templateId, -1, out _, out int itemCount))
                    totalCount += itemCount;
            }
            return (totalCount >= count);
        }

        public int GetItemsCount(uint templateId, int gradeToCount = -1)
        {
            if (GetAllItemsByTemplate(null, templateId, gradeToCount, out var _, out var counted))
                return counted;
            else
                return 0;
        }

        public int GetItemsCount(SlotType slotType, uint templateId, int gradeToCount = -1)
        {
            if (GetAllItemsByTemplate(new SlotType[1] { slotType }, templateId, gradeToCount, out var _, out var counted))
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
            foundItems = new List<Item>();
            unitsOfItemFound = 0;
            if ((inContainerTypes == null) || (inContainerTypes.Length <= 0))
            {
                inContainerTypes = new SlotType[3] { SlotType.Inventory, SlotType.Equipment, SlotType.Bank};
            }
            foreach(var ct in inContainerTypes)
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

        private enum SwapAction
        {
            doNothing,
            doSwap,
            doSplit,
            doMerge,
            doMoveAllToEmpty,
            doEquipInEmptySlot,
        }

        public bool SplitOrMoveItem(ItemTaskType taskType, ulong fromItemId, SlotType fromType, byte fromSlot, ulong toItemId, SlotType toType, byte toSlot, int count = 0)
        {
            var info = string.Format("SplitOrMoveItem({0} {1}:{2} => {3} {4}:{5} - {6})", fromItemId, fromType, fromSlot, toItemId, toType, toSlot, count);
            _log.Debug(info);
            var fromItem = GetItemById(fromItemId);
            if ((fromItem == null) && (fromItemId != 0))
            {
                _log.Error(string.Format("SplitOrMoveItem - ItemId {0} no longer exists, possibly a phantom item.",fromItemId));
                return false;
            }

            var itemInTargetSlot = GetItemById(toItemId);
            var action = SwapAction.doNothing;
            if ((count <= 0) && (fromItem != null))
                count = fromItem.Count;

            // Grab target container for easy manipulation
            ItemContainer targetContainer = Bag;
            ItemContainer sourceContainer = fromItem?._holdingContainer ?? Bag;
            if (_itemContainers.TryGetValue(toType, out targetContainer))
            {
                itemInTargetSlot = targetContainer.GetItemBySlot(toSlot);
            }
            if (itemInTargetSlot == null)
                itemInTargetSlot = targetContainer.GetItemBySlot(toSlot);

            // Are we equipping into a empty slot ? For whatever reason the client will send FROM empty equipment slot => TO item to equip
            if ((fromItemId == 0) && (fromType == SlotType.Equipment) && (toType != SlotType.Equipment) && (itemInTargetSlot != null))
            {
                action = SwapAction.doEquipInEmptySlot;
                sourceContainer = Equipment;
            }

            // Check some conditions when we are not equipping into a empty slot
            if ((action != SwapAction.doEquipInEmptySlot) && (fromItem == null))
            {
                _log.Error("SplitOrMoveItem didn't provide a source itemId");
                return false;
            }
            if ((action != SwapAction.doEquipInEmptySlot) && (fromItem?._holdingContainer?.ContainerType != fromType))
            {
                _log.Error("SplitOrMoveItem Source Item Container did not match what the client asked");
                return false;
            }
            if ((action != SwapAction.doEquipInEmptySlot) && (fromItem.Slot != fromSlot))
            {
                _log.Error("SplitOrMoveItem Source Item slot did not match what the client asked");
                return false;
            }
            if ((action != SwapAction.doEquipInEmptySlot) && (count > fromItem.Count))
            {
                _log.Error("SplitOrMoveItem Source Item has less item count than is requested to be moved");
                return false;
            }
            // Validate target Item stuff
            if (itemInTargetSlot != null)
            {
                if (itemInTargetSlot.SlotType != toType)
                {
                    _log.Error("SplitOrMoveItem Target Item Type does not match");
                    return false;
                }
                if (itemInTargetSlot.Slot != toSlot)
                {
                    _log.Error("SplitOrMoveItem Target Item Slot does not match");
                    return false;
                }
                if ((action != SwapAction.doEquipInEmptySlot) && (itemInTargetSlot.TemplateId == fromItem.TemplateId) && (itemInTargetSlot.Count + count > fromItem.Template.MaxCount))
                {
                    _log.Error("SplitOrMoveItem Target Item stack does not have enough room to take source");
                    return false;
                }
            }

            // Decide what type of thing we need to do
            if (action != SwapAction.doEquipInEmptySlot)
            {
                if ((itemInTargetSlot == null) && (fromItem.Count > count))
                    action = SwapAction.doSplit;
                else
                if ((itemInTargetSlot == null) && (fromItem.Count == count))
                    action = SwapAction.doMoveAllToEmpty;
                else
                if ((itemInTargetSlot != null) && (itemInTargetSlot.TemplateId == fromItem.TemplateId))
                    action = SwapAction.doMerge;
                else
                    action = SwapAction.doSwap;
            }

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
                    else
                        sourceContainer.UpdateFreeSlotCount();
                    break;
                case SwapAction.doMoveAllToEmpty:
                    fromItem.SlotType = targetContainer.ContainerType;
                    fromItem.Slot = toSlot;
                    itemTasks.Add(new ItemMove(fromType, fromSlot, fromItem.Id, toType, toSlot, toItemId));
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
                    var toAddCount = Math.Min(count, itemInTargetSlot.Template.MaxCount - itemInTargetSlot.Count);
                    if (toAddCount < count)
                        _log.Info(string.Format("SplitOrMoveItem supplied more than target can take, changed {0} to {1}",count,toAddCount));
                    itemInTargetSlot.Count += toAddCount;
                    fromItem.Count -= toAddCount;
                    itemTasks.Add(new ItemCountUpdate(itemInTargetSlot, toAddCount));
                    if (fromItem.Count > 0)
                    {
                        itemTasks.Add(new ItemCountUpdate(fromItem, -toAddCount));
                    }
                    else
                    {
                        itemTasks.Add(new ItemRemoveSlot(fromItem));
                        fromItem._holdingContainer.RemoveItem(ItemTaskType.Invalid, fromItem, true);
                    }
                    break;
                case SwapAction.doSwap:
                    // Swap both item slots
                    itemTasks.Add(new ItemMove(fromType, fromSlot, fromItem.Id, toType, toSlot, itemInTargetSlot.Id));
                    fromItem.SlotType = targetContainer.ContainerType;
                    fromItem.Slot = toSlot;
                    if (sourceContainer != targetContainer)
                    {
                        sourceContainer.Items.Remove(fromItem);
                        targetContainer.Items.Add(fromItem);
                        fromItem._holdingContainer = targetContainer;
                        targetContainer.UpdateFreeSlotCount();
                    }
                    itemInTargetSlot.SlotType = sourceContainer.ContainerType;
                    itemInTargetSlot.Slot = fromSlot;
                    if (sourceContainer != targetContainer)
                    {
                        targetContainer.Items.Remove(itemInTargetSlot);
                        sourceContainer.Items.Add(itemInTargetSlot);
                        itemInTargetSlot._holdingContainer = sourceContainer;
                        sourceContainer.UpdateFreeSlotCount();
                    }
                    break;
                default:
                    Owner.SendMessage("|cFFFF0000SplitOrMoveItem swap action not implemented " + action.ToString() + "|r");
                    _log.Info("SplitOrMoveItem swap action not implemented " + action.ToString());
                    break;
            }

            // Handle Equipment Broadcasting
            if (fromType == SlotType.Equipment)
            {
                Owner.BroadcastPacket(
                    new SCUnitEquipmentsChangedPacket(Owner.ObjId, fromSlot, Equipment.GetItemBySlot(fromSlot)), false);
            }

            if (toType == SlotType.Equipment)
            {
                Owner.BroadcastPacket(
                    new SCUnitEquipmentsChangedPacket(Owner.ObjId, toSlot, Equipment.GetItemBySlot(toSlot)), false);
            } 

            if (itemTasks.Count > 0)
                Owner.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));

            sourceContainer.ApplyBindRules(taskType);
            if (targetContainer != sourceContainer)
                targetContainer.ApplyBindRules(taskType);

            return (itemTasks.Count > 0);
        }


        public bool TakeoffBackpack(ItemTaskType taskType,bool glidersOnly = false)
        {
            var backpack = GetEquippedBySlot(EquipmentItemSlot.Backpack);
            if (backpack == null) return true;

            // Check glider if needed
            if ((glidersOnly) && (backpack.Template is BackpackTemplate bt) && (bt.BackpackType != BackpackType.Glider))
                return false;

            // Move to first available slot
            if (Bag.FreeSlotCount <= 0) 
                return false;
            
            Bag.AddOrMoveExistingItem(taskType, backpack);

            return true;
        }

        public Item GetItemById(ulong id)
        {
            foreach(var c in _itemContainers)
            {
                if ((c.Key == SlotType.Equipment) || (c.Key == SlotType.Inventory) || (c.Key == SlotType.Bank))
                {
                    foreach(var i in c.Value.Items)
                    {
                        if ((i != null) && (i.Id == id))
                            return i ;
                    }
                }
            }
            return null;
        }


        public Item GetEquippedBySlot(EquipmentItemSlot slot)
        {
            return Equipment.GetItemBySlot((byte)slot);
        }

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
                case SlotType.Trade:
                    // TODO ...
                    break;
                case SlotType.Mail:
                    // TODO ...
                    break;
            }

            return item;
        }

        public int FreeSlotCount(SlotType type)
        {
            if (_itemContainers.TryGetValue(type, out var c))
                return c.FreeSlotCount;
            else
                return 0;
        }

        private void SendFragmentedInventory(SlotType slotType, byte numItems, Item[] bag)
        {
            var tempItem = new Item[10];

            if ((numItems % 10) != 0)
                _log.Warn("SendFragmentedInventory: Inventory Size not a multiple of 10 ({0})", numItems);
            if (bag.Length != numItems)
                _log.Warn("SendFragmentedInventory: Inventory Size Mismatch; expected {0} got {1}", numItems, bag.Length);

            for (byte chunk = 0; chunk < (numItems / 10); chunk++)
            {
                Array.Copy(bag, chunk * 10, tempItem, 0, 10);
                Owner.SendPacket(new SCCharacterInvenContentsPacket(slotType, 1, chunk, tempItem));
            }
        }

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
                _log.Warn("No Money for expand!");
                return;
            }

            if (expand.ItemId != 0 && expand.ItemCount != 0 && !CheckItems(SlotType.Inventory, expand.ItemId, expand.ItemCount))
            {
                _log.Warn("Item or Count not fount.");
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
                Bag.ConsumeItem(isBank ? ItemTaskType.ExpandBank : ItemTaskType.ExpandBag, expand.ItemId, expand.ItemCount,null);
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
        public void OnAcquiredItem(Item item,int count,bool onlyUpdatedCount = false)
        {
            // Quests
            if ((item?.Template.LootQuestId > 0) && (count != 0))
                Owner?.Quests?.OnItemGather(item, count);
        }

        /// <summary>
        /// Triggers whenever a item (count) is removed
        /// </summary>
        /// <param name="item"></param>
        /// <param name="count"></param>
        /// <param name="onlyUpdatedCount"></param>
        public void OnConsumedItem(Item item, int count, bool onlyUpdatedCount = false)
        {
            // Quests
            if ((item?.Template.LootQuestId > 0) && (count != 0))
                Owner?.Quests?.OnItemGather(item, -count);
        }

    }
}
