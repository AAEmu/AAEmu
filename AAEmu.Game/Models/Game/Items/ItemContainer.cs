using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SQLitePCL;

namespace AAEmu.Game.Models.Game.Items
{
    public class ItemContainer
    {
        private int _containerSize;
        private int _freeSlotCount;
        public Character Owner { get; set; }
        public SlotType ContainerType { get; set; }
        public List<Item> Items { get; set; }
        public bool PartOfPlayerInventory { get ; set; }
        public int ContainerSize
        {
            get
            {
                return _containerSize;
            }
            set
            {
                _containerSize = value;
                UpdateFreeSlotCount();
            }
        }
        public int FreeSlotCount
        {
            get
            {
                return _freeSlotCount;
            }
        }

        public ItemContainer(Character owner, SlotType containerType,bool isPartOfPlayerInventory)
        {
            Owner = owner;
            ContainerType = containerType;
            Items = new List<Item>();
            ContainerSize = -1; // Unlimited
            PartOfPlayerInventory = isPartOfPlayerInventory;
        }

        public void ReNumberSlots(bool reverse = false)
        {
            for (var c = 0; c < Items.Count; c++)
            {
                var i = Items[reverse?Items.Count-1-c:c];
                i.SlotType = ContainerType;
                i.Slot = c;
            }
        }

        public void UpdateFreeSlotCount()
        {
            if (_containerSize < 0)
            {
                _freeSlotCount = 9999; // Should be more than enough
                return;
            }
            Items.Sort();
            var usedSlots = from iSlot in Items select iSlot.Slot;
            var res = 0;
            for (int i = 0; i < _containerSize; i++)
            {
                if (!usedSlots.Contains(i))
                    res++;
            }
            _freeSlotCount = res;
        }

        /// <summary>
        /// Returns a slot index number of the first free location in a inventory
        /// </summary>
        /// <param name="preferredSlot">Preferred location if available</param>
        /// <returns>Location if a empty slot was found, or -1 in case the item container is full</returns>
        private int GetUnusedSlot(int preferredSlot)
        {
            // No max size defined, get the highest number and add one
            if (_containerSize < 0)
            {
                var highestSlot = -1;
                foreach (var i in Items)
                    if (i.Slot > highestSlot)
                        highestSlot = i.Slot;
                highestSlot++;
                return preferredSlot > highestSlot ? preferredSlot : highestSlot;
            }
            // Check the preferred slot to see if it's free, or if we need to assign a new one
            bool needNewSlot = false;
            if (preferredSlot < 0)
            {
                needNewSlot = true;
            }
            else
            {
                foreach (var i in Items)
                {
                    if (i.Slot == preferredSlot)
                    {
                        needNewSlot = true;
                        break;
                    }
                }
            }
            // Find a new slot if needed
            if (needNewSlot)
            {
                var usedSlots = from iSlot in Items where iSlot.Slot != preferredSlot select iSlot.Slot;
                for (int i = 0; i < ContainerSize; i++)
                {
                    if (!usedSlots.Contains(i))
                    {
                        return i;
                    }
                }
                // inventory container is full
                return -1;
            }
            // Otherwise just return the preferred slot
            else
            {
                return preferredSlot;
            }
        }

        public bool TryGetItemBySlot(int slot, out Item theItem)
        {
            foreach (var i in Items)
                if (i.Slot == slot)
                {
                    theItem = i;
                    return true;
                }
            theItem = null;
            return false;
        }

        public Item GetItemBySlot(int slot)
        {
            if (TryGetItemBySlot(slot, out var res))
                return res;
            else
                return null;
        }

        public bool TryGetItemByItemId(ulong item_id, out Item theItem, int grade = -1)
        {
            foreach (var i in Items)
                if (i.Id == item_id && (grade < 0 || i.Grade == grade))
                {
                    theItem = i;
                    return true;
                }
            theItem = null;
            return false;
        }

        public Item GetItemByItemId(ulong item_id, int grade = -1)
        {
            if (TryGetItemByItemId(item_id, out var res, grade))
                return res;
            else
                return null;
        }

        public bool TryGetFirstItemByTemplateId(ulong templateId, out Item theItem, int grade = -1)
        {
            foreach (var i in Items)
                if (i.TemplateId == templateId && (grade < 0 || i.Grade == grade))
                {
                    theItem = i;
                    return true;
                }
            theItem = null;
            return false;
        }

        public Item GetFirstItemByTemplateId(ulong templateId, int grade = -1)
        {
            if (TryGetFirstItemByTemplateId(templateId, out var res, grade))
                return res;
            else
                return null;
        }

        /// <summary>
        /// Adds a Item Object to this container and also updates source container, for new items like craft results, use AcquireDefaultItem instead
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="item">Item Object to add/move to this container</param>
        /// <param name="preferredSlot">preferred slot to place this item in</param>
        /// <returns>Fails on Full Inventory</returns>
        public bool AddOrMoveExistingItem(ItemTaskType taskType, Item item, int preferredSlot = -1)
        {
            if (item == null)
                return false;

            ItemContainer sourceContainer = item?._holdingContainer;
            byte sourceSlot = (byte)item.Slot;
            SlotType sourceSlotType = item.SlotType;

            var currentPreferredSlotItem = GetItemBySlot(preferredSlot);
            var newSlot = -1;
            bool canAddToSameSlot = false;
            // When adding wearables to equipment container, for the slot numbers if needed
            if ((ContainerType == SlotType.Equipment) && (item is EquipItem eItem) && (preferredSlot < 0))
            {
                if (eItem.Template is ArmorTemplate armorTemp)
                    newSlot = (int)armorTemp.SlotTemplate.SlotTypeId;
                if (eItem.Template is AccessoryTemplate accTemp)
                    newSlot = (int)accTemp.SlotTemplate.SlotTypeId;
            }
            if (
                (ContainerType == SlotType.Inventory) && (item.Template.MaxCount > 1) && 
                (currentPreferredSlotItem != null) && 
                (currentPreferredSlotItem.TemplateId == item.TemplateId) && (currentPreferredSlotItem.Grade == item.Grade) && 
                (item.Count + currentPreferredSlotItem.Count <= item.Template.MaxCount))
            {
                newSlot = preferredSlot;
                canAddToSameSlot = true;
            }
            else
            {
                newSlot = GetUnusedSlot(preferredSlot);
                if (newSlot < 0)
                    return false; // Inventory Full
            }

            var itemTasks = new List<ItemTask>();
            var sourceItemTasks = new List<ItemTask>();

            // Only trigger when moving between container with different owners with the exception of this being move to Mail container
            if ((sourceContainer != this) && (item.OwnerId != Owner?.Id) && (this.ContainerType != SlotType.Mail))
            {
                Owner?.Inventory.OnAcquiredItem(item, item.Count);
            }
            else
            // Got attachment from Mail
            if ((item.SlotType == SlotType.Mail) && (this.ContainerType != SlotType.Mail))
            {
                Owner?.Inventory.OnAcquiredItem(item, item.Count);
            }
            else
            // Adding mail attachment
            if ((item.SlotType != SlotType.Mail) && (this.ContainerType == SlotType.Mail))
            {
                Owner?.Inventory.OnConsumedItem(item, item.Count);
            }

            if (canAddToSameSlot)
            {
                currentPreferredSlotItem.Count += item.Count;
                if (this.ContainerType != SlotType.None)
                    itemTasks.Add(new ItemCountUpdate(currentPreferredSlotItem, item.Count));
            }
            else
            {
                item.SlotType = ContainerType;
                item.Slot = newSlot;
                item._holdingContainer = this;
                if (this.Owner != null)
                    item.OwnerId = this.Owner.Id;
                else
                    item.OwnerId = 0;

                Items.Insert(0, item); // insert at front for easy buyback handling
                                       //Items.Add(item);
                UpdateFreeSlotCount();
                // Note we use SlotType.None for things like the Item BuyBack Container. Make sure to manually handle the remove for these
                if (this.ContainerType != SlotType.None)
                    itemTasks.Add(new ItemAdd(item));
            }

            // Item Tasks
            if ((sourceContainer != null) && (sourceContainer != this))
            {
                sourceContainer.Items.Remove(item);
                sourceContainer.UpdateFreeSlotCount();
                if (sourceContainer.ContainerType != SlotType.Mail)
                    sourceItemTasks.Add(new ItemRemoveSlot(item.Id,sourceSlotType,sourceSlot));
            }
            // We use Invalid when doing internals, don't send to client
            if (taskType != ItemTaskType.Invalid)
            {
                if (itemTasks.Count > 0)
                    Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
                if (sourceItemTasks.Count > 0)
                    sourceContainer?.Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, sourceItemTasks, new List<ulong>()));
            }

            ApplyBindRules(taskType);
            return ((itemTasks.Count + sourceItemTasks.Count) > 0);
        }

        /// <summary>
        /// Removes (and Destroys if needed) a item from the container
        /// </summary>
        /// <param name="task"></param>
        /// <param name="item">Item object to be removed</param>
        /// <param name="releaseIdAsWell">Set to true if this item needs to be removed from the world</param>
        /// <returns></returns>
        public bool RemoveItem(ItemTaskType task, Item item, bool releaseIdAsWell)
        {
            Owner?.Inventory.OnConsumedItem(item, item.Count);
            bool res = item._holdingContainer.Items.Remove(item);
            if (res && task != ItemTaskType.Invalid)
                item._holdingContainer?.Owner?.SendPacket(new SCItemTaskSuccessPacket(task, new List<ItemTask> { new ItemRemoveSlot(item) }, new List<ulong>()));
            if (res && releaseIdAsWell)
            {
                item._holdingContainer = null;
                ItemManager.Instance.ReleaseId(item.Id);
            }
            UpdateFreeSlotCount();
            return res;
        }

        /// <summary>
        /// Destroys amountToCosume amount of item units with template templateId from the container
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="templateId">Item templateId to search for</param>
        /// <param name="amountToConsume">Amount of item units to consume</param>
        /// <param name="preferredItem">If not null, use this Item as primairy source for consume</param>
        /// <returns>The amount of items that was actually consumed, 0 when failed or not found</returns>
        public int ConsumeItem(ItemTaskType taskType, uint templateId, int amountToConsume,Item preferredItem)
        {
            if (!GetAllItemsByTemplate(templateId, -1, out var foundItems, out var count))
                return 0; // Nothing found
            if (amountToConsume > count)
                return 0; // Not enough total

            if ((preferredItem != null) && (templateId != preferredItem.TemplateId))
                return 0; // Preferred item template did not match the requested template
            // TODO: implement use of preferredItem (required for using specific items when you have more than one stack)

            var totalConsumed = 0;
            var itemTasks = new List<ItemTask>();

            // Try to consume preferred item first
            if ((amountToConsume > 0) && (preferredItem != null))
            {
                // Remove this entry from our list
                if (!foundItems.Remove(preferredItem))
                {
                    // Preferred item was not found in our list of found items, something is wrong here
                    return 0;
                }
                
                var toRemove = Math.Min(preferredItem.Count, amountToConsume);
                preferredItem.Count -= toRemove;
                amountToConsume -= toRemove;

                if (preferredItem.Count > 0)
                {
                    Owner?.Inventory.OnConsumedItem(preferredItem, toRemove);
                    itemTasks.Add(new ItemCountUpdate(preferredItem, -toRemove));
                }
                else
                {
                    RemoveItem(taskType, preferredItem, true); // Normally, this can never fail
                }

                totalConsumed += toRemove;
            }

            // Check all remaining items
            if (amountToConsume > 0)
            {
                foreach (var i in foundItems)
                {
                    var toRemove = Math.Min(i.Count, amountToConsume);
                    i.Count -= toRemove;
                    amountToConsume -= toRemove;

                    if (i.Count > 0)
                    {
                        Owner?.Inventory.OnConsumedItem(i, toRemove);
                        itemTasks.Add(new ItemCountUpdate(i, -toRemove));
                    }
                    else
                    {
                        RemoveItem(taskType, i, true); // Normally, this can never fail
                    }

                    totalConsumed += toRemove;
                    if (amountToConsume <= 0)
                        break; // We are done with the list, leave the rest as is
                }
            }

            // We use Invalid when doing internals, don't send to client
            if (taskType != ItemTaskType.Invalid)
                Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
            UpdateFreeSlotCount();
            return totalConsumed;
        }

        /// <summary>
        /// Adds items to container using templateId and gradeToAdd, if items aren't full stacks, those will be updated first, new items will be generated for the remaining amounts
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="templateId">Item templateId use for adding</param>
        /// <param name="amountToAdd">Number of item units to add</param>
        /// <param name="gradeToAdd">Overrides default grade if possible</param>
        /// <returns></returns>
        public bool AcquireDefaultItem(ItemTaskType taskType, uint templateId, int amountToAdd, int gradeToAdd = -1, uint crafterId = 0)
        {
            return AcquireDefaultItemEx(taskType, templateId, amountToAdd, gradeToAdd, out _, out _, crafterId);
        }

        /// <summary>
        /// Adds items to container using templateId and gradeToAdd, if items aren't full stacks, those will be updated first, new items will be generated for the remaining amounts
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="templateId">Item templateId use for adding</param>
        /// <param name="amountToAdd">Number of item units to add</param>
        /// <param name="gradeToAdd">Overrides default grade if possible</param>
        /// <param name="updatedItemsList">A List of the newly added or updated items</param>
        /// <returns></returns>
        public bool AcquireDefaultItemEx(ItemTaskType taskType, uint templateId, int amountToAdd, int gradeToAdd, out List<Item> newItemsList, out List<Item> updatedItemsList, uint crafterId)
        {
            newItemsList = new List<Item>();
            updatedItemsList = new List<Item>();
            if (amountToAdd <= 0)
                return true;

            GetAllItemsByTemplate(templateId, gradeToAdd, out var currentItems, out var currentTotalItemCount);
            var template = ItemManager.Instance.GetTemplate(templateId);
            if (template == null)
                return false; // Invalid item templateId
            var totalFreeSpaceForThisItem = (currentItems.Count * template.MaxCount) - currentTotalItemCount + (FreeSlotCount * template.MaxCount);

            // Trying to add too many item units to this container ?
            if (amountToAdd > totalFreeSpaceForThisItem)
                return false;

            // Calculate grade to actually add for new items
            if ((template.FixedGrade >= 0) && (template.Gradable == false))
                gradeToAdd = template.FixedGrade;
            if (gradeToAdd == -1)
                gradeToAdd = template.FixedGrade;
            if (gradeToAdd < 0)
                gradeToAdd = 0;


            // First try to add to existing item counts
            var itemTasks = new List<ItemTask>();
            foreach (var i in currentItems)
            {
                var freeSpace = i.Template.MaxCount - i.Count;
                if (freeSpace > 0)
                {
                    var addAmount = Math.Min(freeSpace, amountToAdd);
                    i.Count += addAmount;
                    amountToAdd -= addAmount;
                    itemTasks.Add(new ItemCountUpdate(i, addAmount));
                    updatedItemsList.Add(i);
                    Owner?.Inventory.OnAcquiredItem(i, addAmount, true);
                }
                if (amountToAdd < 0)
                    break;
            }
            while (amountToAdd > 0)
            {
                var addAmount = Math.Min(amountToAdd, template.MaxCount);
                var newItem = ItemManager.Instance.Create(templateId, addAmount, (byte)gradeToAdd, true);
                // Add name if marked as crafter (single stack items only)
                if ((crafterId > 0) && (newItem.Template.MaxCount == 1))
                {
                    newItem.MadeUnitId = crafterId;
                    newItem.WorldId = 1; // TODO: proper world id handling
                }
                amountToAdd -= addAmount;
                var prefSlot = -1;
                if ((newItem.Template is BackpackTemplate) && (ContainerType == SlotType.Equipment))
                    prefSlot = (int)EquipmentItemSlot.Backpack;
                if (AddOrMoveExistingItem(ItemTaskType.Invalid, newItem,prefSlot)) // Task set to invalid as we send our own packets inside this function
                {
                    itemTasks.Add(new ItemAdd(newItem));
                    newItemsList.Add(newItem);
                }
                else
                    throw new Exception("AcquireDefaultItem(); Unable to add new items"); // Inventory should have enough space, something went wrong
            }
            if (taskType != ItemTaskType.Invalid)
                Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
            UpdateFreeSlotCount();
            return (itemTasks.Count > 0);
        }

        /// <summary>
        /// Count the maximum amount of items of a given templateID that can be added to a inventory taking into account the max stack size. Ignores item grade
        /// </summary>
        /// <param name="templateId">Item template ID</param>
        /// <returns>Amount of item units that can be added before the bag is full</returns>
        public int SpaceLeftForItem(uint templateId)
        {
            GetAllItemsByTemplate(templateId, -1, out var currentItems, out var currentTotalItemCount);
            var template = ItemManager.Instance.GetTemplate(templateId);
            if (template == null)
                return 0 ; // Invalid item templateId
            return (currentItems.Count * template.MaxCount) - currentTotalItemCount + (FreeSlotCount * template.MaxCount);
        }

        /// <summary>
        /// Count the maximum amount of items of a given item that can be added to a inventory taking into account the max stack size using a specific item to be added. Takes into account item grade
        /// </summary>
        /// <param name="itemToAdd">Item we wish to add for</param>
        /// <param name="currentItems">List of items in the current container that match the itemToAdd's criteria (template and grade)</param>
        /// <returns>Amount of item units of the given item that can be added before the bag is full</returns>
        public int SpaceLeftForItem(Item itemToAdd, out List<Item> currentItems)
        {
            if (itemToAdd == null)
            {
                currentItems = new List<Item>();
                return 0;
            }
            GetAllItemsByTemplate(itemToAdd.TemplateId, itemToAdd.Grade, out currentItems, out var currentTotalItemCount);
            return (currentItems.Count * itemToAdd.Template.MaxCount) - currentTotalItemCount + (FreeSlotCount * itemToAdd.Template.MaxCount);
        }

        /// <summary>
        /// Returns a list of items in the order of their slot, unused slots return null
        /// </summary>
        /// <returns>Ordered list slots with items</returns>
        public List<Item> GetSlottedItemsList()
        {
            var res = new List<Item>(ContainerSize);
            for(int i = 0; i < ContainerSize;i++)
                res.Add(GetItemBySlot(i));
            return res;
        }

        /// <summary>
        /// Searches container for a list of items that have a specified templateId
        /// </summary>
        /// <param name="templateId">templateId to search for</param>
        /// <param name="foundItems">List of found item objects</param>
        /// <param name="gradeToFind">Only lists items of specific grade is gradeToFind >= </param>
        /// <param name="unitsOfItemFound">Total count of the count values of the found items</param>
        /// <returns>True if any item was found</returns>
        public bool GetAllItemsByTemplate(uint templateId, int gradeToFind, out List<Item> foundItems, out int unitsOfItemFound)
        {
            foundItems = new List<Item>();
            unitsOfItemFound = 0;
            foreach (var i in Items)
                if ((i.TemplateId == templateId) && ((gradeToFind < 0) || (gradeToFind == i.Grade)))
                {
                    foundItems.Add(i);
                    unitsOfItemFound += i.Count;
                }
            return (foundItems.Count > 0);
        }

        public void ApplyBindRules(ItemTaskType taskType)
        {
            var itemTasks = new List<ItemTask>();
            foreach(var i in Items)
            {
                if (i.HasFlag(ItemFlag.SoulBound) == false)
                {
                    if ((ContainerType == SlotType.Inventory) && (i.Template.BindType == ItemBindType.BindOnPickup))
                        i.SetFlag(ItemFlag.SoulBound);

                    if ((ContainerType == SlotType.Equipment) && (i.Template.BindType == ItemBindType.BindOnEquip))
                        i.SetFlag(ItemFlag.SoulBound);

                    if (i.HasFlag(ItemFlag.SoulBound))
                    {
                        itemTasks.Add(new ItemUpdateBits(i));
                    }
                }
            }
            if (itemTasks.Count > 0)
                Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
        }

        /// <summary>
        /// Removes and released all items
        /// </summary>
        public void Wipe()
        {
            while(Items.Count > 0)
                RemoveItem(ItemTaskType.Invalid, Items[0], true);
            UpdateFreeSlotCount();
        }

    }

}
