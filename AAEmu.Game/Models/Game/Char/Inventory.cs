using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.DB;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{

    public class Inventory
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private int _freeSlot;
        private int _freeBankSlot;

        public readonly Character Owner;

        // public Item[] Equip { get; set; }
        // public Item[] Items { get; set; }
        // public Item[] Bank { get; set; }

        public Dictionary<SlotType, ItemContainer> _itemContainers { get; set; }
        public ItemContainer Equipment { get; private set; }
        public ItemContainer PlayerInventory { get; private set; }
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
                var newContainer = new ItemContainer(owner, st);
                _itemContainers.Add(st, newContainer);
                switch (st)
                {
                    case SlotType.Equipment:
                        newContainer.ContainerSize = 28;
                        Equipment = newContainer;
                        break;
                    case SlotType.Inventory:
                        newContainer.ContainerSize = Owner.NumInventorySlots;
                        PlayerInventory = newContainer;
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

            //Equip = new Item[28];
            //Items = new Item[Owner.NumInventorySlots];
            //Bank = new Item[Owner.NumBankSlots];
        }

        #region Database

        public void Load(MySqlConnection connection, SlotType? slotType = null)
        {

            var playeritems = ItemManager.Instance.LoadPlayerInventory(Owner);
            foreach (var container in _itemContainers)
            {
                container.Value.Items.Clear();
                container.Value.UpdateFreeSlotCount();
            }
            foreach (var item in playeritems)
            {
                if (_itemContainers.TryGetValue(item.SlotType, out var container))
                {
                    if (!container.AddOrMoveExistingItem(ItemTaskType.Invalid, item, item.Slot))
                    {
                        _log.Fatal("LoadInventory found unused item type for item, Id {0} for {1}", item.SlotType, Owner?.Name ?? "<system>");
                        throw new Exception(string.Format("Was unable to add item {0} to container {1} for player {2} using the defined slot.", item?.Template.Name ?? item.TemplateId.ToString(), item.Slot.ToString(), Owner?.Name ?? "???"));
                    }
                }
                else
                {
                    _log.Warn("LoadInventory found unused itemId {0} for {1}", item.SlotType, Owner?.Name ?? "<system>");
                }
            }

            /*
            using (var command = connection.CreateCommand())
            {
                if (slotType == null)
                    command.CommandText = "SELECT * FROM items WHERE `owner` = @owner";
                else
                {
                    command.CommandText = "SELECT * FROM items WHERE `owner` = @owner AND `slot_type` = @slot_type";
                    command.Parameters.AddWithValue("@slot_type", slotType);
                }

                command.Parameters.AddWithValue("@owner", Owner.Id);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var type = reader.GetString("type");
                        Type nClass = null;
                        try
                        {
                            nClass = Type.GetType(type);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);
                        }

                        if (nClass == null)
                        {
                            _log.Error("Item type {0} not found!", type);
                            continue;
                        }

                        Item item;
                        try
                        {
                            item = (Item)Activator.CreateInstance(nClass);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);
                            _log.Error(ex.InnerException);
                            item = new Item();
                        }

                        item.Id = reader.GetUInt64("id");
                        item.TemplateId = reader.GetUInt32("template_id");
                        item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);
                        item.SlotType = (SlotType)Enum.Parse(typeof(SlotType), reader.GetString("slot_type"), true);
                        item.Slot = reader.GetInt32("slot");
                        item.Count = reader.GetInt32("count");
                        item.LifespanMins = reader.GetInt32("lifespan_mins");
                        item.MadeUnitId = reader.GetUInt32("made_unit_id");
                        item.UnsecureTime = reader.GetDateTime("unsecure_time");
                        item.UnpackTime = reader.GetDateTime("unpack_time");
                        item.CreateTime = reader.GetDateTime("created_at");
                        item.Bounded = reader.GetByte("bounded");
                        var details = (PacketStream)(byte[])reader.GetValue("details");
                        item.ReadDetails(details);

                        if (item.Template.FixedGrade >= 0)
                            item.Grade = (byte)item.Template.FixedGrade; // Overwrite Fixed-grade items, just to make sure
                        else if (item.Template.Gradable)
                            item.Grade = reader.GetByte("grade"); // Load from our DB if the item is gradable

                        if (item.SlotType == SlotType.Equipment)
                            Equip[item.Slot] = item;
                        else if (item.SlotType == SlotType.Inventory)
                            Items[item.Slot] = item;
                        else if (item.SlotType == SlotType.Bank)
                            Bank[item.Slot] = item;
                        else if (item.SlotType == SlotType.Mail)
                            MailItems.Add(item);
                    }
                }
            }
            */

            /*
            if (slotType == null || slotType == SlotType.Equipment)
                foreach (var item in Equipment.Items.Where(x => x != null))
                    item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);
            */
            if (slotType == null || slotType == SlotType.Inventory)
            {
                foreach (var item in PlayerInventory.Items.Where(x => x != null))
                    item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);
                _freeSlot = FreeSlotCount(SlotType.Inventory);
            }

            if (slotType == null || slotType == SlotType.Bank)
            {
                foreach (var item in Warehouse.Items.Where(x => x != null))
                    item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);

                _freeBankSlot = FreeSlotCount(SlotType.Bank);
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            /*
            lock (_removedItems)
            {
                if (_removedItems.Count > 0)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "DELETE FROM items WHERE owner= @owner AND id IN(" + string.Join(",", _removedItems) + ")";
                        command.Prepare();
                        command.Parameters.AddWithValue("@owner", Owner.Id);
                        command.ExecuteNonQuery();
                    }

                    _removedItems.Clear();
                }
            }

            SaveItems(connection, transaction, Equip);
            SaveItems(connection, transaction, Items);
            SaveItems(connection, transaction, Bank);
            SaveItems(connection, transaction, MailItems.ToArray());
            */
        }

        private void SaveItems(MySqlConnection connection, MySqlTransaction transaction, Item[] items)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                foreach (var item in items)
                {
                    if (item == null)
                        continue;
                    var details = new PacketStream();
                    item.WriteDetails(details);

                    command.CommandText = "REPLACE INTO " +
                                          "items(`id`,`type`,`template_id`,`slot_type`,`slot`,`count`,`details`,`lifespan_mins`,`made_unit_id`,`unsecure_time`,`unpack_time`,`owner`,`created_at`,`grade`, `bounded`)" +
                                          " VALUES " +
                                          "(@id,@type,@template_id,@slot_type,@slot,@count,@details,@lifespan_mins,@made_unit_id,@unsecure_time,@unpack_time,@owner,@created_at,@grade,@bounded)";

                    command.Parameters.AddWithValue("@id", item.Id);
                    command.Parameters.AddWithValue("@type", item.GetType().ToString());
                    command.Parameters.AddWithValue("@template_id", item.TemplateId);
                    command.Parameters.AddWithValue("@slot_type", (byte)item.SlotType);
                    command.Parameters.AddWithValue("@slot", item.Slot);
                    command.Parameters.AddWithValue("@count", item.Count);
                    command.Parameters.AddWithValue("@details", details.GetBytes());
                    command.Parameters.AddWithValue("@lifespan_mins", item.LifespanMins);
                    command.Parameters.AddWithValue("@made_unit_id", item.MadeUnitId);
                    command.Parameters.AddWithValue("@unsecure_time", item.UnsecureTime);
                    command.Parameters.AddWithValue("@unpack_time", item.UnpackTime);
                    command.Parameters.AddWithValue("@created_at", item.CreateTime);
                    command.Parameters.AddWithValue("@owner", item.OwnerId);
                    command.Parameters.AddWithValue("@grade", item.Grade);
                    command.Parameters.AddWithValue("@bounded", (byte)item.ItemFlags);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }
        }

        #endregion

        public void Send()
        {
            Owner.SendPacket(new SCCharacterInvenInitPacket(Owner.NumInventorySlots, (uint)Owner.NumBankSlots));
            SendFragmentedInventory(SlotType.Inventory, Owner.NumInventorySlots, PlayerInventory.GetSlottedItemsList().ToArray());
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

            /*
            if (item.Slot == -1)
            {
                var fItemIndex = -1;
                for (var i = 0; i < Items.Length; i++)
                    if (Items[i]?.Template != null && Items[i].Template.Id == item.Template.Id &&
                        Items[i].Template.MaxCount >= Items[i].Count + item.Count)
                    {
                        fItemIndex = i;
                        break;
                    }

                if (fItemIndex == -1)
                    item.Slot = _freeSlot;
                else
                {
                    var fItem = Items[fItemIndex];
                    fItem.Count += item.Count;
                    ItemManager.Instance.ReleaseId(item.Id);
                                        
                    if (item.Template.LootQuestId > 0)
                        Owner.Quests.OnItemGather(item, item.Count);
                    
                    return fItem;
                }
            }

            if (item.Slot == -1 && _freeSlot == -1)
                return null;

            if (Items[item.Slot] == null)
            {
                item.SlotType = SlotType.Inventory;
                Items[item.Slot] = item;

                _freeSlot = CheckFreeSlot(SlotType.Inventory);

                if (item.Template.LootQuestId > 0)
                    Owner.Quests.OnItemGather(item, item.Count);

                return item;
            }

            return null;
            */
        }

        public void RemoveItem(ItemTaskType taskType, Item item, bool release)
        {
            foreach (var c in _itemContainers)
                c.Value.RemoveItem(taskType, item, release);
            /*
            if (item.SlotType == SlotType.Equipment)
                Equip[item.Slot] = null;
            else if (item.SlotType == SlotType.Inventory)
            {
                Items[item.Slot] = null;
                if (_freeSlot == -1 || item.Slot < _freeSlot)
                    _freeSlot = item.Slot;
            }
            else if (item.SlotType == SlotType.Bank)
            {
                Bank[item.Slot] = null;
                if (_freeBankSlot == -1 || item.Slot < _freeBankSlot)
                    _freeBankSlot = item.Slot;
            }

            if (release)
                ItemManager.Instance.ReleaseId(item.Id);
            */
        }

        /*
        public List<(Item Item, int Count)> RemoveItem(uint templateId, int count)
        {
            // TODO: Make a ConsumeItem(uint templateId, int count) function instead

            var res = new List<(Item, int)>();
            foreach (var item in PlayerInventory.Items)
                if (item != null && item.TemplateId == templateId)
                {
                    var itemCount = item.Count;
                    var temp = Math.Min(count, itemCount);
                    item.Count -= temp;
                    count -= temp;
                    if (count < 0)
                        count = 0;
                    if (item.Count == 0)
                    {
                        Items[item.Slot] = null;
                        if (_freeSlot == -1 || item.Slot < _freeSlot)
                            _freeSlot = item.Slot;
                        ItemManager.Instance.ReleaseId(item.Id);
                    }

                    res.Add((item, itemCount - item.Count));
                    if (count == 0)
                        break;
                }

            return res;
        }
        */

        public bool CheckItems(SlotType slotType, uint templateId, int count)
        {
            var totalCount = 0;
            if (_itemContainers.TryGetValue(slotType, out var c))
            {
                if (c.GetAllItemsByTemplate(templateId, out _, out int itemCount))
                    totalCount += itemCount;
            }
            if (totalCount <= 0)
                return false;


            Item[] items = null;
            if (slotType == SlotType.Inventory)
                items = PlayerInventory.GetSlottedItemsList().ToArray();
            else if (slotType == SlotType.Equipment)
                items = Equipment.GetSlottedItemsList().ToArray();
            else if (slotType == SlotType.Bank)
                items = Warehouse.GetSlottedItemsList().ToArray();

            if (items == null)
                return false;

            foreach (var item in items)
                if (item != null && item.TemplateId == templateId)
                {
                    count -= item.Count;
                    if (count < 0)
                        count = 0;
                    if (count == 0)
                        break;
                }

            return count == 0;
        }

        public int GetItemsCount(uint templateId)
        {
            if (GetAllItemsByTemplate(null, templateId, out var _, out var counted))
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
        /// <param name="unitsOfItemFound">Total count of the count values of the found items</param>
        /// <returns>True if any item was found</returns>
        public bool GetAllItemsByTemplate(SlotType[] inContainerTypes, uint templateId, out List<Item> foundItems, out int unitsOfItemFound)
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
                    res |= c.GetAllItemsByTemplate(templateId, out var theseItems, out var theseAmounts);
                    foundItems.AddRange(theseItems);
                    unitsOfItemFound += theseAmounts;
                }
            }
            return res;
        }

        public void Move(ulong fromItemId, SlotType fromType, byte fromSlot, ulong toItemId, SlotType toType, byte toSlot, int count = 0)
        {
            // TODO: rewrite this
            if (count < 0)
                count = 0;

            var fromItem = GetItem(fromType, fromSlot);
            var toItem = GetItem(toType, toSlot);

            if (fromItem != null && fromItem.Id != fromItemId)
            {
                _log.Warn("ItemMove: {0} {1}", fromItem.Id, fromItemId);
                // TODO ... ItemNotify?
                return;
            }

            if (toItem != null && toItem.Id != toItemId)
            {
                _log.Warn("ItemMove: {0} {1}", toItem.Id, toItemId);
                // TODO ... ItemNotify?
                return;
            }

            var removingItems = new List<ulong>();
            var tasks = new List<ItemTask>();

            tasks.Add(new ItemMove(fromType, fromSlot, fromItemId, toType, toSlot, toItemId));

            if (fromType == SlotType.Equipment)
            {
                Equipment.AddOrMoveExistingItem(ItemTaskType.SwapItems, toItem, fromSlot);
                //Equip[fromSlot] = toItem;
            }
            else if (fromType == SlotType.Inventory)
            {
                PlayerInventory.AddOrMoveExistingItem(ItemTaskType.SwapItems, toItem, fromSlot);
                //Items[fromSlot] = toItem;
            }
            else if (fromType == SlotType.Bank)
            {
                Warehouse.AddOrMoveExistingItem(ItemTaskType.SwapItems, toItem, fromSlot);
                //Bank[fromSlot] = toItem;
            }

            if (toType == SlotType.Equipment)
            {
                Equipment.AddOrMoveExistingItem(ItemTaskType.SwapItems, fromItem, toSlot);
                // Equip[toSlot] = fromItem;
            }
            else if (toType == SlotType.Inventory)
            {
                PlayerInventory.AddOrMoveExistingItem(ItemTaskType.SwapItems, fromItem, toSlot);
                //Items[toSlot] = fromItem;
            }
            else if (toType == SlotType.Bank)
            {
                Warehouse.AddOrMoveExistingItem(ItemTaskType.SwapItems, fromItem, toSlot);
                // Bank[toSlot] = fromItem;
            }

            if (fromItem != null)
            {
                fromItem.SlotType = toType;
                fromItem.Slot = toSlot;
            }

            if (toItem != null)
            {
                toItem.SlotType = fromType;
                toItem.Slot = fromSlot;
            }

            _freeSlot = FreeSlotCount(SlotType.Inventory);
            _freeBankSlot = FreeSlotCount(SlotType.Bank);

            if (toItem != null && toItem.Template.BindId == ItemBindType.BindOnEquip && toItem.ItemFlags.HasFlag(ItemFlag.SoulBound))
            {
                toItem.ItemFlags |= ItemFlag.SoulBound ;
                tasks.Add(new ItemUpdateBits(toItem, toItem.ItemFlags));
            }

            Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems, tasks, removingItems));

            if (fromType == SlotType.Equipment)
                Owner.BroadcastPacket(
                    new SCUnitEquipmentsChangedPacket(Owner.ObjId, new[]
                    {
                        (fromSlot, Equipment.GetItemBySlot(fromSlot))
                    }), false);
            if (toType == SlotType.Equipment)
                Owner.BroadcastPacket(
                    new SCUnitEquipmentsChangedPacket(Owner.ObjId, new[]
                    {
                        (toSlot, Equipment.GetItemBySlot(toSlot))
                    }), false);
        }

        public bool TakeoffBackpack()
        {
            var backpack = GetEquippedBySlot(EquipmentItemSlot.Backpack);
            if (backpack == null) return true;

            // Move to first available slot
            var slot = FreeSlotCount(SlotType.Inventory);
            if (slot == -1) return false;

            Move(backpack.Id, SlotType.Equipment, (byte)EquipmentItemSlot.Backpack, 0, SlotType.Inventory, (byte)slot, 1);
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

        /*
        public Item GetItemByTemplateId(ulong templateId)
        {
            foreach (var item in Equipment.Items)
                if (item != null && item.TemplateId == templateId)
                    return item;
            foreach (var item in Items)
                if (item != null && item.TemplateId == templateId)
                    return item;
            return null;
        }
        */

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
                    item = PlayerInventory.GetItemBySlot(slot);
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
                PlayerInventory.ConsumeItem(isBank ? ItemTaskType.ExpandBank : ItemTaskType.ExpandBag, expand.ItemId, expand.ItemCount);
                /*
                var items = RemoveItem(expand.ItemId, expand.ItemCount);

                foreach (var (item, count) in items)
                {
                    if (item.Count == 0)
                        tasks.Add(new ItemRemove(item));
                    else
                        tasks.Add(new ItemCountUpdate(item, -count));
                }
                */
            }

            //Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems, tasks, new List<ulong>()));
            if (isBank)
            {
                Owner.NumBankSlots = (short)(50 + 10 * (1 + step));
                Warehouse.ContainerSize = Owner.NumBankSlots;
            }
            else
            {
                Owner.NumInventorySlots = (byte)(50 + 10 * (1 + step));
                PlayerInventory.ContainerSize = Owner.NumInventorySlots;
            }

            Owner.SendPacket(
                new SCInvenExpandedPacket(
                    isBank ? SlotType.Bank : SlotType.Inventory,
                    isBank ? (byte)Owner.NumBankSlots : Owner.NumInventorySlots
                )
            );
        }

    }
}
