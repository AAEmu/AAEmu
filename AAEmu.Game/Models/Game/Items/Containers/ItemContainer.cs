using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Exceptions;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class ItemContainer
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private int _containerSize;
    private int _freeSlotCount;
    private ICharacter _owner;
    private uint _ownerId;
    public bool IsDirty { get; set; }
    private readonly SlotType _containerType;
    private ulong _containerId;

    public Unit ParentUnit { get; set; }

    public ICharacter Owner
    {
        get
        {
            if (_owner == null && _ownerId > 0)
            {
                _owner = WorldManager.Instance.GetCharacterById(_ownerId);
            }

            return _owner;
        }
        set
        {
            _owner = value;
            if (value?.Id != _ownerId)
            {
                _ownerId = value?.Id ?? 0;
                IsDirty = true;
            }
        }
    }

    public uint OwnerId
    {
        get => _owner?.Id ?? _ownerId;
        private init
        {
            if (value != _ownerId)
            {
                _ownerId = value;
                IsDirty = true;
            }

            _owner = null; // this will make it so that it will try to fetch on the next query
        }
    }

    public uint MateId { get; set; }

    public SlotType ContainerType
    {
        get => _containerType;
        private init
        {
            if (value != _containerType)
            {
                _containerType = value;
                IsDirty = true;
            }
        }
    }

    public ulong ContainerId
    {
        get => _containerId;
        set
        {
            if (value != _containerId)
            {
                _containerId = value;
                IsDirty = true;
            }
        }
    }

    public List<Item> Items { get; set; }

    private bool PartOfPlayerInventory => ContainerType switch
    {
        SlotType.Invalid => false,
        SlotType.Equipment => true,
        SlotType.Bag => true,
        SlotType.Bank => true,
        SlotType.Coffer => true,
        SlotType.MailAttachment => false,
        SlotType.Money => false,
        SlotType.PetRideEquipment => false,
        SlotType.SlaveEquipment => false,
        SlotType.StoreGood => false,
        SlotType.Auction => false,
        _ => throw new ArgumentOutOfRangeException()
    };

    public int ContainerSize
    {
        get => _containerSize;
        set
        {
            if (value != _containerSize)
            {
                _containerSize = value;
                IsDirty = true;
            }

            UpdateFreeSlotCount();
        }
    }

    public int FreeSlotCount => _freeSlotCount;

    protected ItemContainer()
    {
        // Only relevant for inheritance
        Owner = null;
        ContainerType = SlotType.Invalid;
        Items = new List<Item>();
        ContainerSize = 0;
    }

    /// <summary>
    /// Creates a Container
    /// </summary>
    /// <param name="ownerId">Player Id for this container</param>
    /// <param name="containerType"></param>
    /// <param name="createWithNewId"></param>
    /// <param name="parentUnit">Parent that will actually hold this container (can be different from Owner)</param>
    public ItemContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit)
    {
        OwnerId = ownerId;
        ContainerType = containerType;
        ParentUnit = parentUnit;
        Items = new List<Item>();
        ContainerSize = -1; // Unlimited
        if (createWithNewId)
        {
            ContainerId = ContainerIdManager.Instance.GetNextId();
        }
    }

    public void ReNumberSlots(bool reverse = false)
    {
        for (var c = 0; c < Items.Count; c++)
        {
            var i = Items[reverse ? Items.Count - 1 - c : c];
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

        var usedSlotsCount = Items.Count(i => i != null);
        _freeSlotCount = _containerSize - usedSlotsCount;
    }

    /// <summary>
    /// Returns a slot index number of the first free location in an inventory
    /// </summary>
    /// <param name="preferredSlot">Preferred location if available</param>
    /// <returns>Location if an empty slot was found, or -1 in case the item container is full</returns>
    public int GetUnusedSlot(int preferredSlot)
    {
        // No max size defined, get the highest number and add one
        if (_containerSize < 0)
        {
            var highestSlot = -1;
            foreach (var i in Items)
            {
                if (i.Slot > highestSlot)
                {
                    highestSlot = i.Slot;
                }
            }

            highestSlot++;
            return preferredSlot > highestSlot ? preferredSlot : highestSlot;
        }

        // Check the preferred slot to see if it's free, or if we need to assign a new one
        var needNewSlot = false;
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
            var usedSlots = (from iSlot in Items where iSlot.Slot != preferredSlot select iSlot.Slot).ToList();
            for (var i = 0; i < ContainerSize; i++)
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

    private bool TryGetItemBySlot(int slot, out Item theItem)
    {
        foreach (var i in Items)
        {
            if (i.Slot == slot)
            {
                theItem = i;
                return true;
            }
        }

        theItem = null;
        return false;
    }

    public Item GetItemBySlot(int slot)
    {
        if (TryGetItemBySlot(slot, out var res))
        {
            return res;
        }
        else
        {
            return null;
        }
    }

    private bool TryGetItemByItemId(ulong itemId, out Item theItem)
    {
        foreach (var i in Items)
        {
            if (i.Id == itemId)
            {
                theItem = i;
                return true;
            }
        }

        theItem = null;
        return false;
    }

    public Item GetItemByItemId(ulong itemId)
    {
        if (TryGetItemByItemId(itemId, out var res))
        {
            return res;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Adds an Item Object to this container and also updates source container, for new items like craft results, use AcquireDefaultItem instead
    /// </summary>
    /// <param name="taskType"></param>
    /// <param name="item">Item Object to add/move to this container</param>
    /// <param name="preferredSlot">preferred slot to place this item in</param>
    /// <returns>Fails on Full Inventory or if target slot is invalid</returns>
    public bool AddOrMoveExistingItem(ItemTaskType taskType, Item item, int preferredSlot = -1)
    {
        if (item == null)
        {
            return false;
        }

        var sourceContainer = item._holdingContainer;
        var sourceSlot = (byte)item.Slot;
        var sourceSlotType = item.SlotType;

        var currentPreferredSlotItem = GetItemBySlot(preferredSlot);
        var newSlot = -1;
        var canAddToSameSlot = false;

        // When adding wearables to equipment container, for the slot numbers if needed
        if (this is EquipmentContainer && item is EquipItem _ && preferredSlot < 0)
        {
            var validSlots = EquipmentContainer.GetAllowedGearSlots(item.Template);
            // find valid empty slot (if any), stop looking if it is the preferred slot
            foreach (var vSlot in validSlots)
            {
                if (GetItemBySlot((int)vSlot) == null)
                {
                    newSlot = (int)vSlot;
                    break;
                }
            }
        }

        // Make sure the item is in container size's range
        if (
            ContainerType == SlotType.Bag && item.Template.MaxCount > 1 &&
            currentPreferredSlotItem != null &&
            currentPreferredSlotItem.TemplateId == item.TemplateId && currentPreferredSlotItem.Grade == item.Grade &&
            item.Count + currentPreferredSlotItem.Count <= item.Template.MaxCount)
        {
            newSlot = preferredSlot;
            canAddToSameSlot = true;
        }
        else
        {
            if (newSlot < 0)
            {
                newSlot = GetUnusedSlot(preferredSlot);
            }

            if (newSlot < 0)
            {
                return false; // Inventory Full
            }
        }

        // Check if the newSlot fits
        if (!CanAccept(item, newSlot))
        {
            return false;
        }

        var itemTasks = new List<ItemTask>();
        var sourceItemTasks = new List<ItemTask>();

        if (canAddToSameSlot)
        {
            currentPreferredSlotItem.Count += item.Count;
            if (ContainerType != SlotType.Invalid)
            {
                itemTasks.Add(new ItemCountUpdate(currentPreferredSlotItem, item.Count));
            }
        }
        else
        {
            item.SlotType = ContainerType;
            item.Slot = newSlot;
            item._holdingContainer = this;
            item.OwnerId = OwnerId;

            Items.Insert(0, item); // insert at front for easy buyback handling

            UpdateFreeSlotCount();

            // Note we use SlotType.None for things like the Item BuyBack Container. Make sure to manually handle the remove for these
            if (ContainerType != SlotType.Invalid)
            {
                itemTasks.Add(new ItemAdd(item));
            }

            if (sourceContainer != this)
            {
                sourceContainer?.OnLeaveContainer(item, this, sourceSlot);
                OnEnterContainer(item, sourceContainer, sourceSlot);
            }
        }

        // Item Tasks
        if (sourceContainer != null && sourceContainer != this)
        {
            sourceContainer.Items.Remove(item);
            sourceContainer.IsDirty = true;
            sourceContainer.UpdateFreeSlotCount();
            if (sourceContainer.ContainerType != SlotType.MailAttachment)
            {
                sourceItemTasks.Add(new ItemRemoveSlot(item.Id, sourceSlotType, sourceSlot));
            }
        }

        // We use Invalid when doing internals, don't send to client
        if (taskType != ItemTaskType.Invalid)
        {
            if (itemTasks.Count > 0)
            {
                Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
            }

            if (sourceItemTasks.Count > 0)
            {
                sourceContainer?.Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, sourceItemTasks, new List<ulong>()));
            }
        }

        ApplyBindRules(taskType);

        // Moved to the end of the method so that the item is already in the inventory
        // Only trigger when moving between containers with different owners except for this being move to Mail container
        //if ((sourceContainer != this) && (item.OwnerId != OwnerId) && (this.ContainerType != SlotType.MailAttachment))
        if (sourceContainer != this && ContainerType != SlotType.MailAttachment)
        {
            Owner?.Inventory.OnAcquiredItem(item, item.Count);
        }
        else
        // Got attachment from Mail
        if (item.SlotType == SlotType.MailAttachment && ContainerType != SlotType.MailAttachment)
        {
            Owner?.Inventory.OnAcquiredItem(item, item.Count);
        }
        else
        // Adding mail attachment
        if (item.SlotType != SlotType.MailAttachment && ContainerType == SlotType.MailAttachment)
        {
            Owner?.Inventory.OnConsumedItem(item, item.Count);
        }

        return itemTasks.Count + sourceItemTasks.Count > 0;
    }

    /// <summary>
    /// Adds an Item Object to this container and also updates source container, for new items like craft results, use AcquireDefaultItem instead
    /// </summary>
    /// <param name="taskType"></param>
    /// <param name="item">Item Object to add/move to this container</param>
    /// <param name="preferredSlot">preferred slot to place this item in</param>
    /// <returns>Fails on Full Inventory or if target slot is invalid</returns>
    public bool AddOrMoveExistingItem2(ItemTaskType taskType, Item item, int preferredSlot = -1)
    {
        if (item == null)
        {
            return false;
        }

        var sourceContainer = item._holdingContainer;
        var sourceSlot = (byte)item.Slot;
        var sourceSlotType = item.SlotType;

        var currentPreferredSlotItem = GetItemBySlot(preferredSlot);
        var newSlot = -1;
        var canAddToSameSlot = false;

        // When adding wearables to equipment container, for the slot numbers if needed
        if (this is EquipmentContainer && item is EquipItem _ && preferredSlot < 0)
        {
            var validSlots = EquipmentContainer.GetAllowedGearSlots(item.Template);
            // find valid empty slot (if any), stop looking if it is the preferred slot
            foreach (var vSlot in validSlots)
            {
                if (GetItemBySlot((int)vSlot) == null)
                {
                    newSlot = (int)vSlot;
                    break;
                }
            }
        }

        // Make sure the item is in container size's range
        if (
            ContainerType == SlotType.Bag && item.Template.MaxCount > 1 &&
            currentPreferredSlotItem != null &&
            currentPreferredSlotItem.TemplateId == item.TemplateId && currentPreferredSlotItem.Grade == item.Grade &&
            item.Count + currentPreferredSlotItem.Count <= item.Template.MaxCount)
        {
            newSlot = preferredSlot;
            canAddToSameSlot = true;
        }
        else
        {
            if (newSlot < 0)
            {
                newSlot = GetUnusedSlot(preferredSlot);
            }

            if (newSlot < 0)
            {
                return false; // Inventory Full
            }
        }

        // Check if the newSlot fits
        if (!CanAccept(item, newSlot))
        {
            return false;
        }

        var itemTasks = new List<ItemTask>();
        var sourceItemTasks = new List<ItemTask>();

        if (canAddToSameSlot)
        {
            currentPreferredSlotItem.Count += item.Count;
            if (ContainerType != SlotType.Invalid)
            {
                itemTasks.Add(new ItemCountUpdate(currentPreferredSlotItem, item.Count));
            }
        }
        else
        {
            item.SlotType = ContainerType;
            item.Slot = newSlot;
            item._holdingContainer = this;
            item.OwnerId = OwnerId;

            Items.Insert(0, item); // insert at front for easy buyback handling

            UpdateFreeSlotCount();

            // Note we use SlotType.None for things like the Item BuyBack Container. Make sure to manually handle the remove for these
            if (ContainerType != SlotType.Invalid)
            {
                itemTasks.Add(new ItemAdd(item));
            }

            if (sourceContainer != this)
            {
                sourceContainer?.OnLeaveContainer(item, sourceContainer, sourceSlot);
                OnEnterContainer(item, this, (byte)newSlot);
            }
        }

        // Item Tasks
        if (sourceContainer?.ContainerType is not (SlotType.MailAttachment and SlotType.Auction))
        {
            if (sourceContainer is not null && sourceContainer != this)
            {
                sourceContainer.Items.Remove(item);
                sourceContainer.IsDirty = true;
                sourceContainer.UpdateFreeSlotCount();
                if (sourceContainer.ContainerType != SlotType.MailAttachment || sourceContainer.ContainerType != SlotType.Auction)
                {
                    sourceItemTasks.Add(new ItemRemoveSlot(item.Id, sourceSlotType, sourceSlot));
                }
            }
        }

        // We use Invalid when doing internals, don't send to client
        if (taskType != ItemTaskType.Invalid)
        {
            if (itemTasks.Count > 0)
            {
                Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
            }

            if (sourceItemTasks.Count > 0)
            {
                sourceContainer?.Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, sourceItemTasks, new List<ulong>()));
            }
        }

        ApplyBindRules(taskType);

        // Moved to the end of the method so that the item is already in the inventory
        // Only trigger when moving between containers with different owners except for this being move to Mail container
        //if ((sourceContainer != this) && (item.OwnerId != OwnerId) && (this.ContainerType != SlotType.MailAttachment))
        if (sourceContainer != this && (ContainerType != SlotType.MailAttachment || ContainerType != SlotType.Auction))
        {
            Owner?.Inventory.OnAcquiredItem(item, item.Count);
        }
        else
        // Got attachment from Mail
        if (item.SlotType == SlotType.MailAttachment && ContainerType != SlotType.MailAttachment)
        {
            Owner?.Inventory.OnAcquiredItem(item, item.Count);
        }
        else
        // Adding mail attachment
        if (item.SlotType != SlotType.MailAttachment && ContainerType == SlotType.MailAttachment)
        {
            Owner?.Inventory.OnConsumedItem(item, item.Count);
        }
        else
        // Got attachment from Auction
        if (item.SlotType == SlotType.Auction && ContainerType != SlotType.Auction)
        {
            Owner?.Inventory.OnAcquiredItem(item, item.Count);
        }
        else
        // Adding Auction attachment
        if (item.SlotType != SlotType.Auction && ContainerType == SlotType.Auction)
        {
            Owner?.Inventory.OnConsumedItem(item, item.Count);
        }

        return itemTasks.Count + sourceItemTasks.Count > 0;
    }

    /// <summary>
    /// Removes (and Destroys if needed) an item from the container
    /// </summary>
    /// <param name="task"></param>
    /// <param name="item">Item object to be removed</param>
    /// <param name="releaseIdAsWell">Set to true if this item needs to be removed from the world</param>
    /// <returns></returns>
    public bool RemoveItem(ItemTaskType task, Item item, bool releaseIdAsWell)
    {
        if (!item.CanDestroy())
        {
            return false;
        }

        var oldSlotNumber = (byte)item.Slot;

        // Handle items that can expire
        GamePacket sync = null;
        if (item.ExpirationOnlineMinutesLeft > 0.0 || item.ExpirationTime > DateTime.UtcNow || item.UnpackTime > DateTime.UtcNow)
        {
            sync = ItemManager.ExpireItemPacket(item);
        }

        if (sync != null)
        {
            Owner?.SendPacket(sync);
        }

        var res = item._holdingContainer.Items.Remove(item);
        if (res && task != ItemTaskType.Invalid)
        {
            item._holdingContainer?.Owner?.SendPacket(new SCItemTaskSuccessPacket(task, [new ItemRemoveSlot(item)], []));
        }

        if (res && releaseIdAsWell)
        {
            item._holdingContainer = null;
            ItemManager.Instance.ReleaseId(item.Id);
        }

        UpdateFreeSlotCount();

        Owner?.Inventory.OnConsumedItem(item, item.Count);
        OnLeaveContainer(item, null, oldSlotNumber);

        return res;
    }

    /// <summary>
    /// Destroys amountToConsume amount of item units with template templateId from the container
    /// </summary>
    /// <param name="taskType"></param>
    /// <param name="templateId">Item templateId to search for</param>
    /// <param name="amountToConsume">Amount of item units to consume</param>
    /// <param name="preferredItem">If not null, use this Item as primary source for consume</param>
    /// <returns>The amount of items that was actually consumed, 0 when failed or not found</returns>
    public int ConsumeItem(ItemTaskType taskType, uint templateId, int amountToConsume, Item preferredItem)
    {
        if (!GetAllItemsByTemplate(templateId, -1, out var foundItems, out _))
        {
            return 0; // Nothing found
        }

        if (preferredItem != null && templateId != preferredItem.TemplateId)
        {
            return 0; // Preferred item template did not match the requested template
        }

        var totalConsumed = 0;
        var itemTasks = new List<ItemTask>();

        // Try to consume preferred item first
        if (amountToConsume > 0 && preferredItem != null)
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
                itemTasks.Add(new ItemCountUpdate(preferredItem, -toRemove));
            }
            else
            {
                RemoveItem(taskType, preferredItem, true); // Normally, this can never fail
            }

            Owner?.Inventory.OnConsumedItem(preferredItem, toRemove);

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
                {
                    break; // We are done with the list, leave the rest as is
                }
            }
        }

        // We use Invalid when doing internals, don't send to client
        if (taskType != ItemTaskType.Invalid)
        {
            Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
        }

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
    /// <param name="crafterId"></param>
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
    /// <param name="newItemsList"></param>
    /// <param name="updatedItemsList">A List of the newly added or updated items</param>
    /// <param name="crafterId"></param>
    /// <param name="preferredSlot"></param>
    /// <returns></returns>
    public bool AcquireDefaultItemEx(ItemTaskType taskType, uint templateId, int amountToAdd, int gradeToAdd, out List<Item> newItemsList, out List<Item> updatedItemsList, uint crafterId, int preferredSlot = -1)
    {
        newItemsList = new List<Item>();
        updatedItemsList = new List<Item>();
        if (amountToAdd <= 0)
        {
            return true;
        }

        //GetAllItemsByTemplate(templateId, gradeToAdd, out var currentItems, out var currentTotalItemCount);
        GetAllItemsByTemplate(templateId, out var currentItems, out var currentTotalItemCount);
        var template = ItemManager.Instance.GetTemplate(templateId);
        if (template == null)
        {
            return false; // Invalid item templateId
        }

        var totalFreeSpaceForThisItem = currentItems.Count * template.MaxCount - currentTotalItemCount + FreeSlotCount * template.MaxCount;

        // Trying to add too many item units to this container ?
        if (amountToAdd > totalFreeSpaceForThisItem)
        {
            return false;
        }

        // Calculate grade to actually add for new items
        if (template.FixedGrade >= 0 && template.Gradable == false)
        {
            gradeToAdd = template.FixedGrade;
        }

        if (gradeToAdd == -1)
        {
            gradeToAdd = template.FixedGrade;
        }

        if (gradeToAdd < 0)
        {
            gradeToAdd = 0;
        }

        // First try to add to existing item counts
        var itemTasks = new List<ItemTask>();

        // Never update in mail containers
        if (ContainerType != SlotType.MailAttachment)
        {
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
                {
                    break;
                }
            }
        }

        var syncPackets = new List<GamePacket>();
        while (amountToAdd > 0)
        {
            var addAmount = Math.Min(amountToAdd, template.MaxCount);
            var newItem = ItemManager.Instance.Create(templateId, addAmount, (byte)gradeToAdd);
            if (newItem == null)
            {
                Logger.Error($"Failed to add item with ID {templateId}, possible duplicate entries!");
                return false;
            }

            // Add name if marked as crafter (single stack items only)
            if (crafterId > 0 && newItem.Template.MaxCount == 1)
            {
                newItem.MadeUnitId = crafterId;
                newItem.WorldId = (byte)WorldManager.DefaultWorldId; // TODO: proper world id handling, this should actually be the ServerId
            }

            amountToAdd -= addAmount;
            var prefSlot = preferredSlot;
            if (newItem.Template is BackpackTemplate && ContainerType == SlotType.Equipment)
            {
                prefSlot = (int)EquipmentItemSlot.Backpack;
            }

            // Timers
            if (newItem.Template.ExpAbsLifetime > 0)
            {
                syncPackets.Add(ItemManager.SetItemExpirationTime(newItem, DateTime.UtcNow.AddMinutes(newItem.Template.ExpAbsLifetime)));
            }

            if (newItem.Template.ExpOnlineLifetime > 0)
            {
                syncPackets.Add(ItemManager.SetItemOnlineExpirationTime(newItem, newItem.Template.ExpOnlineLifetime));
            }

            if (newItem.Template.ExpDate > 0)
            {
                syncPackets.Add(ItemManager.SetItemExpirationTime(newItem, DateTime.UtcNow.AddMinutes(newItem.Template.ExpDate)));
            }

            if (newItem is EquipItem equipItem && newItem.Template is EquipItemTemplate equipItemTemplate)
            {
                equipItem.ChargeCount = equipItemTemplate.ChargeCount;
                if (equipItemTemplate.ChargeLifetime > 0 && equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) == false)
                {
                    equipItem.ChargeStartTime = DateTime.UtcNow;
                }
            }

            if (AddOrMoveExistingItem(ItemTaskType.Invalid, newItem, prefSlot)) // Task set to invalid as we send our own packets inside this function
            {
                itemTasks.Add(new ItemAdd(newItem));
                newItemsList.Add(newItem);
            }
            else
            {
                throw new GameException("AcquireDefaultItem(); Unable to add new items"); // Inventory should have enough space, something went wrong
            }
        }

        if (taskType != ItemTaskType.Invalid)
        {
            Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
        }

        UpdateFreeSlotCount();

        // Send item expire packets if needed
        foreach (var sync in syncPackets)
        {
            if (sync != null)
            {
                Owner?.SendPacket(sync);
            }
        }

        return itemTasks.Count > 0;
    }

    /// <summary>
    /// Count the maximum amount of items of a given templateID that can be added to an inventory taking into account the max stack size. Ignores item grade
    /// </summary>
    /// <param name="templateId">Item template ID</param>
    /// <returns>Amount of item units that can be added before the bag is full</returns>
    public int SpaceLeftForItem(uint templateId)
    {
        GetAllItemsByTemplate(templateId, -1, out var currentItems, out var currentTotalItemCount);
        var template = ItemManager.Instance.GetTemplate(templateId);
        if (template == null)
        {
            return 0; // Invalid item templateId
        }

        return currentItems.Count * template.MaxCount - currentTotalItemCount + FreeSlotCount * template.MaxCount;
    }

    /// <summary>
    /// Count the maximum amount of items of a given item that can be added to an inventory taking into account the max stack size using a specific item to be added. Takes into account item grade
    /// </summary>
    /// <param name="itemToAdd">Item we wish to add for</param>
    /// <param name="currentItems">List of items in the current container that match the itemToAdd criteria (template and grade)</param>
    /// <returns>Amount of item units of the given item that can be added before the bag is full</returns>
    public int SpaceLeftForItem(Item itemToAdd, out List<Item> currentItems)
    {
        if (itemToAdd == null)
        {
            currentItems = new List<Item>();
            return 0;
        }

        GetAllItemsByTemplate(itemToAdd.TemplateId, itemToAdd.Grade, out currentItems, out var currentTotalItemCount);
        return currentItems.Count * itemToAdd.Template.MaxCount - currentTotalItemCount + FreeSlotCount * itemToAdd.Template.MaxCount;
    }

    /// <summary>
    /// Returns a list of items in the order of their slot, unused slots return null
    /// </summary>
    /// <returns>Ordered list slots with items</returns>
    public List<Item> GetSlottedItemsList()
    {
        var res = new List<Item>(ContainerSize);
        for (var i = 0; i < ContainerSize; i++)
        {
            res.Add(GetItemBySlot(i));
        }

        return res;
    }

    /// <summary>
    /// Searches container for a list of items that have a specified templateId
    /// </summary>
    /// <param name="templateId">templateId to search for</param>
    /// <param name="foundItems">List of found item objects</param>
    /// <param name="gradeToFind">Only lists items of specific grade equal to gradeToFind or any grade if -1 was provided</param>
    /// <param name="unitsOfItemFound">Total count of the count values of the found items</param>
    /// <returns>True if any item was found</returns>
    public bool GetAllItemsByTemplate(uint templateId, int gradeToFind, out List<Item> foundItems, out int unitsOfItemFound)
    {
        foundItems = new List<Item>();
        unitsOfItemFound = 0;
        foreach (var i in Items)
        {
            if (i.TemplateId == templateId && (gradeToFind < 0 || gradeToFind == i.Grade))
            {
                foundItems.Add(i);
                unitsOfItemFound += i.Count;
            }
        }

        return foundItems.Count > 0;
    }

    public bool GetAllItemsByTemplate(uint templateId, out List<Item> foundItems, out int unitsOfItemFound)
    {
        foundItems = new List<Item>();
        unitsOfItemFound = 0;
        foreach (var i in Items.Where(i => i.TemplateId == templateId))
        {
            foundItems.Add(i);
            unitsOfItemFound += i.Count;
        }
        return foundItems.Count > 0;
    }

    /// <summary>
    /// Apply Bound flag to items when needed by the container (BindOnPickup, BindOnEquip)
    /// </summary>
    /// <param name="taskType"></param>
    public void ApplyBindRules(ItemTaskType taskType)
    {
        var itemTasks = new List<ItemTask>();
        foreach (var item in Items)
        {
            if (item.HasFlag(ItemFlag.SoulBound) == false)
            {
                if (ContainerType == SlotType.Bag && item.Template.BindType == ItemBindType.BindOnPickup)
                {
                    item.SetFlag(ItemFlag.SoulBound);
                }

                if (ContainerType == SlotType.Equipment && item.Template.BindType == ItemBindType.BindOnEquip)
                {
                    item.SetFlag(ItemFlag.SoulBound);
                }

                if (item.HasFlag(ItemFlag.SoulBound))
                {
                    itemTasks.Add(new ItemUpdateBits(item));
                }
            }
        }

        if (itemTasks.Count > 0)
        {
            Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, itemTasks, new List<ulong>()));
        }
    }

    /// <summary>
    /// Removes and released all items
    /// </summary>
    public void Wipe()
    {
        while (Items.Count > 0)
        {
            RemoveItem(ItemTaskType.Invalid, Items[0], true);
        }

        UpdateFreeSlotCount();
    }

    public virtual bool CanAccept(Item item, int targetSlot)
    {
        if (item == null)
        {
            return true;
        }

        // When it's a backpack, allow only gliders by default
        if (PartOfPlayerInventory && item.Template is BackpackTemplate backpackTemplate)
        {
            return backpackTemplate.BackpackType == BackpackType.Glider || backpackTemplate.BackpackType == BackpackType.ToyFlag;
        }

        return true;
    }

    /// <summary>
    /// Creates a ItemContainer or descendant base of the name of the container type
    /// </summary>
    /// <param name="containerTypeName"></param>
    /// <param name="ownerId">Player Id that owns the items in this container</param>
    /// <param name="slotType"></param>
    /// <param name="createWithNewId"></param>
    /// <param name="parentUnit">Actual unit that will hold this container</param>
    /// <returns></returns>
    public static ItemContainer CreateByTypeName(string containerTypeName, uint ownerId, SlotType slotType, bool createWithNewId, Unit parentUnit)
    {
        if (containerTypeName.EndsWith("SlaveEquipmentContainer"))
            return new SlaveEquipmentContainer(ownerId, slotType, createWithNewId, parentUnit);

        if (containerTypeName.EndsWith("MateEquipmentContainer"))
        {
            return new MateEquipmentContainer(ownerId, slotType, createWithNewId, parentUnit);
        }

        if (containerTypeName.EndsWith("EquipmentContainer"))
        {
            return new EquipmentContainer(ownerId, slotType, createWithNewId, parentUnit);
        }

        if (containerTypeName.EndsWith("CofferContainer"))
        {
            return new CofferContainer(ownerId, createWithNewId);
        }

        // Fall-back
        return new ItemContainer(ownerId, slotType, createWithNewId, parentUnit);
    }

    public string ContainerTypeName()
    {
        var cName = GetType().Name;
        if (cName.Contains('.'))
        {
            cName = cName.Substring(cName.LastIndexOf(".", StringComparison.InvariantCulture) + 1);
        }

        return cName;
    }

    public virtual void Delete()
    {
        ItemManager.Instance.DeleteItemContainer(this);
    }

    public virtual void OnEnterContainer(Item item, ItemContainer lastContainer, byte previousSlot)
    {
        item._holdingContainer = this; // назначим новый контейнер
        //item.SlotType = ContainerType;
        //item.Slot = previousSlot;
        item.OwnerId = OwnerId;
    }

    public virtual void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot)
    {
        // Do Nothing
        //item._holdingContainer = null; // назначим новый контейнер
        //item.SlotType = SlotType.Invalid;
        //item.Slot = previousSlot;
        //item.OwnerId = OwnerId;
    }
}
