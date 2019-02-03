using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class Inventory
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private int _freeSlot;
        private int _freeBankSlot;
        private List<ulong> _removedItems;

        public readonly Character Owner;
        public Item[] Equip { get; set; }
        public Item[] Items { get; set; }
        public Item[] Bank { get; set; }

        public Inventory(Character owner)
        {
            Owner = owner;
            Equip = new Item[28];
            Items = new Item[Owner.NumInventorySlots];
            Bank = new Item[Owner.NumBankSlots];
            _removedItems = new List<ulong>();
        }

        #region Database

        public void Load(MySqlConnection connection, SlotType? slotType = null)
        {
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
                            item = (Item) Activator.CreateInstance(nClass);
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
                        item.SlotType = (SlotType) Enum.Parse(typeof(SlotType), reader.GetString("slot_type"), true);
                        item.Slot = reader.GetInt32("slot");
                        item.Count = reader.GetInt32("count");
                        item.LifespanMins = reader.GetInt32("lifespan_mins");
                        item.UnsecureTime = reader.GetDateTime("unsecure_time");
                        item.UnpackTime = reader.GetDateTime("unpack_time");
                        item.CreateTime = reader.GetDateTime("created_at");
                        var details = (PacketStream) (byte[]) reader.GetValue("details");
                        item.ReadDetails(details);

                        if (item.SlotType == SlotType.Equipment)
                            Equip[item.Slot] = item;
                        else if (item.SlotType == SlotType.Inventory)
                            Items[item.Slot] = item;
                        else if (item.SlotType == SlotType.Bank)
                            Bank[item.Slot] = item;
                    }
                }
            }

            if (slotType == null || slotType == SlotType.Equipment)
                foreach (var item in Equip.Where(x => x != null))
                    item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);

            if (slotType == null || slotType == SlotType.Inventory)
            {
                foreach (var item in Items.Where(x => x != null))
                    item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);
                _freeSlot = CheckFreeSlot(SlotType.Inventory);
            }

            if (slotType == null || slotType == SlotType.Bank)
            {
                foreach (var item in Bank.Where(x => x != null))
                    item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);

                _freeBankSlot = CheckFreeSlot(SlotType.Bank);
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
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
                                          "items(`id`,`type`,`template_id`,`slot_type`,`slot`,`count`,`details`,`lifespan_mins`,`unsecure_time`,`unpack_time`,`owner`,`created_at`)" +
                                          " VALUES " +
                                          "(@id,@type,@template_id,@slot_type,@slot,@count,@details,@lifespan_mins,@unsecure_time,@unpack_time,@owner,@created_at)";

                    command.Parameters.AddWithValue("@id", item.Id);
                    command.Parameters.AddWithValue("@type", item.GetType().ToString());
                    command.Parameters.AddWithValue("@template_id", item.TemplateId);
                    command.Parameters.AddWithValue("@slot_type", (byte) item.SlotType);
                    command.Parameters.AddWithValue("@slot", item.Slot);
                    command.Parameters.AddWithValue("@count", item.Count);
                    command.Parameters.AddWithValue("@details", details.GetBytes());
                    command.Parameters.AddWithValue("@lifespan_mins", item.LifespanMins);
                    command.Parameters.AddWithValue("@unsecure_time", item.UnsecureTime);
                    command.Parameters.AddWithValue("@unpack_time", item.UnpackTime);
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.Parameters.AddWithValue("@created_at", item.CreateTime);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }
        }

        #endregion

        public void Send()
        {
            Owner.SendPacket(new SCCharacterInvenInitPacket(Owner.NumInventorySlots, (uint) Owner.NumBankSlots));
            Owner.SendPacket(new SCCharacterInvenContentsPacket(SlotType.Inventory, (byte)(Owner.NumInventorySlots / 10), 0, Items));
            Owner.SendPacket(new SCCharacterInvenContentsPacket(SlotType.Bank, (byte)(Owner.NumBankSlots / 10), 0, Bank));
        }

        public Item AddItem(Item item)
        {
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
                    ItemIdManager.Instance.ReleaseId((uint) item.Id);
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

                return item;
            }

            return null;
        }

        public void RemoveItem(Item item, bool release)
        {
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
                ItemIdManager.Instance.ReleaseId((uint) item.Id);
            lock (_removedItems)
            {
                if (!_removedItems.Contains(item.Id))
                    _removedItems.Add(item.Id);
            }
        }

        public void Move(ulong fromItemId, SlotType fromType, byte fromSlot, ulong toItemId, SlotType toType, byte toSlot, int count = 0)
        {
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
                Equip[fromSlot] = toItem;
            else if (fromType == SlotType.Inventory)
                Items[fromSlot] = toItem;
            else if (fromType == SlotType.Bank)
                Bank[fromSlot] = toItem;

            if (toType == SlotType.Equipment)
                Equip[toSlot] = fromItem;
            else if (toType == SlotType.Inventory)
                Items[toSlot] = fromItem;
            else if (toType == SlotType.Bank)
                Bank[toSlot] = fromItem;

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

            _freeSlot = CheckFreeSlot(SlotType.Inventory);
            _freeBankSlot = CheckFreeSlot(SlotType.Bank);

            Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SwapItems, tasks, removingItems));

            if (fromType == SlotType.Equipment)
                Owner.BroadcastPacket(
                    new SCUnitEquipmentsChangedPacket(Owner.ObjId, new[]
                    {
                        (fromSlot, Equip[fromSlot])
                    }), false);
            if (toType == SlotType.Equipment)
                Owner.BroadcastPacket(
                    new SCUnitEquipmentsChangedPacket(Owner.ObjId, new[]
                    {
                        (toSlot, Equip[toSlot])
                    }), false);
        }

        public Item GetItem(ulong id)
        {
            foreach (var item in Equip)
                if (item != null && item.Id == id)
                    return item;
            foreach (var item in Items)
                if (item != null && item.Id == id)
                    return item;
            return null;
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
                    item = Equip[slot];
                    break;
                case SlotType.Inventory:
                    item = Items[slot];
                    break;
                case SlotType.Bank:
                    item = Bank[slot];
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

        public int CheckFreeSlot(SlotType type)
        {
            var slot = 0;
            if (type == SlotType.Inventory)
            {
                while (Items[slot] != null)
                    slot++;
                if (slot > Items.Length)
                    slot = -1;
            }
            else if (type == SlotType.Bank)
            {
                while (Bank[slot] != null)
                    slot++;
                if (slot > Bank.Length)
                    slot = -1;
            }

            return slot;
        }
    }
}
