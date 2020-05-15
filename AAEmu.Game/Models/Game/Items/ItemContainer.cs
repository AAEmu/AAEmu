using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Items
{
    public class ItemContainer
    {
        private int _containerSize;
        private int _freeSlotCount;
        public uint OwnerId { get; set; }
        public SlotType ContainerType { get; set; }
        public List<Item> Items { get; set; }
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

        public ItemContainer(uint owner_id, SlotType containerType)
        {
            OwnerId = owner_id;
            ContainerType = containerType;
            Items = new List<Item>();
            ContainerSize = -1; // Unlimited
        }

        public void ReNumberSlots()
        {
            var c = 0;
            foreach (var i in Items)
            {
                i.SlotType = ContainerType;
                i.Slot = c;
                c++;
            }
        }

        public void UpdateFreeSlotCount()
        {
            if (_containerSize < 0)
            {
                _freeSlotCount = int.MaxValue;
                return;
            }
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

        public bool AddOrMoveItem(Item item, int preferredSlot = -1)
        {
            ItemContainer sourceContainer = item?._holdingContainer;
            var newSlot = GetUnusedSlot(preferredSlot);
            if (newSlot < 0)
                return false; // Inventory Full
            item.SlotType = ContainerType;
            item.Slot = newSlot;
            item._holdingContainer = this;
            item.OwnerId = this.OwnerId ;
            Items.Add(item);
            UpdateFreeSlotCount();
            if ((sourceContainer != null) && (sourceContainer != this))
            {
                sourceContainer.Items.Remove(item);
                sourceContainer.UpdateFreeSlotCount();
            }
            return true;
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

        public bool TryGetItemByItemId(ulong item_id, out Item theItem)
        {
            foreach (var i in Items)
                if (i.Id == item_id)
                {
                    theItem = i;
                    return true;
                }
            theItem = null;
            return false;
        }

        public Item GetItemByItemId(ulong item_id)
        {
            if (TryGetItemByItemId(item_id, out var res))
                return res;
            else
                return null;
        }

        public bool RemoveItem(Item item, bool releaseIdAsWell)
        {
            bool res = item._holdingContainer.Items.Remove(item);
            if (res && releaseIdAsWell)
                ItemIdManager.Instance.ReleaseId((uint)item.Id);
            return res;
        }

        public List<Item> GetSlottedItemsList()
        {
            var res = new List<Item>(ContainerSize);
            for(int i = 0; i < ContainerSize;i++)
                res.Add(GetItemBySlot(i));
            return res;
        }

    }

}
