using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeSlaveEquipmentPacket : GamePacket
{
    public CSChangeSlaveEquipmentPacket() : base(CSOffsets.CSChangeSlaveEquipmentPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadUInt32();
        var tl = stream.ReadUInt16(); // slave tl
        var DbSlaveId = stream.ReadUInt32();
        var bts = stream.ReadBoolean();
        var num = stream.ReadByte();

        Logger.Debug($"ChangeSlaveEquipment, TlId: {tl}, Id: {characterId}, Id2: {DbSlaveId}, BTS: {bts}, num: {num}");

        var slave = SlaveManager.Instance.GetSlaveByTlId(tl);
        if (slave == null)
        {
            Logger.Warn($"ChangeSlaveEquipment, Unable to find slave with tlId {tl}!");
            return;
        }
        if (num == 0)
            return;
        //                  SlotType, Slot, Item
        var invItems = new (SlotType, byte, Item)[num];
        var equipItems = new (SlotType, byte, Item)[num];
        var character = Connection.ActiveChar;

        for (var i = 0; i < num; i++)
        {
            invItems[i].Item3 = new Item();
            invItems[i].Item3.Read(stream);
            invItems[i].Item3.ItemFlags = ItemFlag.SoulBound; // связанный
            invItems[i].Item3.ChargeUseSkillTime = DateTime.UtcNow;

            equipItems[i].Item3 = new Item();
            equipItems[i].Item3.Read(stream);
            equipItems[i].Item3.ItemFlags = ItemFlag.SoulBound; // связанный
            equipItems[i].Item3.ChargeUseSkillTime = DateTime.UtcNow;

            invItems[i].Item1 = (SlotType)stream.ReadByte();
            invItems[i].Item2 = stream.ReadByte();

            equipItems[i].Item1 = (SlotType)stream.ReadByte();
            equipItems[i].Item2 = stream.ReadByte();

            var isEquip = invItems[i].Item3.TemplateId != 0;

            invItems[i].Item3 = character.Inventory.Bag.GetItemBySlot(invItems[i].Item2);
            equipItems[i].Item3 = slave.Equipment.GetItemBySlot(equipItems[i].Item2);

            Logger.Debug($"FROM: ({invItems[i].Item1}:{invItems[i].Item2}) TO ({equipItems[i].Item1}:{equipItems[i].Item2}) ITEMS: {invItems[i].Item3?.Id}, {equipItems[i].Item3?.Id}, EQUIP: {isEquip}");

            if (isEquip)
            {
                if (invItems[i].Item3 != null)
                {
                    var itemTasks = new List<ItemTask>();
                    itemTasks.Add(new ItemRemove(invItems[i].Item3));

                    if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, character.Inventory.Bag, slave.Equipment, invItems[i].Item3.Id, invItems[i].Item1, invItems[i].Item2, 0, equipItems[i].Item1, equipItems[i].Item2))
                    {
                        Connection.SendPacket(new SCSlaveEquipmentChangedPacket(invItems[i], equipItems[i], tl, characterId, slave.Id, bts));
                        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, itemTasks, []));
                        //SlaveManager.Instance.Update(character, slave, invItems[i].Item3.TemplateId, invItems[i].Item3, isEquip);
                    }
                }
            }
            else
            {
                if (equipItems[i].Item3 != null)
                {
                    if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, slave.Equipment, character.Inventory.Bag, equipItems[i].Item3.Id, equipItems[i].Item1, equipItems[i].Item2, 0, invItems[i].Item1, invItems[i].Item2))
                    {
                        Connection.SendPacket(new SCSlaveEquipmentChangedPacket(invItems[i], equipItems[i], tl, characterId, slave.Id, bts));
                        //SlaveManager.Instance.Update(character, slave, equipItems[i].Item3.TemplateId, equipItems[i].Item3, isEquip);
                    }
                }
            }
        }

        ////slaveEquipment = new SlaveEquipment();
        ////slaveEquipment.Read(stream);

        //////var id = stream.ReadUInt32(); // type (id)
        //////var tl = stream.ReadUInt16();
        //////var dbSlaveId = stream.ReadUInt32();
        //////var bts = stream.ReadBoolean();
        //////var num = stream.ReadByte();
        //////for (var i = 0; i < num; i++)
        //////{
        //////    // read item1
        //////    var item1 = new Item();
        //////    item1.Read(stream);

        //////    // read item2
        //////    var item2 = new Item();
        //////    item2.Read(stream);

        //////    var slotType1 = (SlotType)stream.ReadByte();   // type
        //////    var slot1 = stream.ReadByte();            // index

        //////    var slotType2 = (SlotType)stream.ReadByte();  // type
        //////    var slot2 = stream.ReadByte();           // index
        //////}

        ////Logger.Debug($"ChangeSlaveEquipment, Id: {slaveEquipment.Id}, Tl: {slaveEquipment.Tl}, DbSlaveId: {slaveEquipment.DbSlaveId}, Bts: {slaveEquipment.Bts}");

        ////var slave = SlaveManager.Instance.GetSlaveByOwnerObjId(Connection.ActiveChar.ObjId);

        ////Connection.SendPacket(new SCSlaveEquipmentChangedPacket(slaveEquipment, true));
        ////Connection.ActiveChar.BroadcastPacket(new SCUnitEquipmentsChangedPacket(slave.ObjId, slaveEquipment.Slot, null), false);
    }
}
