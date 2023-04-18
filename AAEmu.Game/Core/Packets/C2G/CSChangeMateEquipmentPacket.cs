using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeMateEquipmentPacket : GamePacket
    {
        public CSChangeMateEquipmentPacket() : base(CSOffsets.CSChangeMateEquipmentPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var unkId = stream.ReadUInt32();
            var tl = stream.ReadUInt16();
            var unk2Id = stream.ReadUInt32();
            var bts = stream.ReadBoolean();
            var num = stream.ReadByte();

            _log.Debug("ChangeMateEquipment, TlId: {0}, Id: {1}, Id2: {2}, BTS: {3}, num: {4}", tl, unkId, unk2Id, bts, num);

            var mate = MateManager.Instance.GetActiveMateByTlId(tl);

            if (mate == null)
            {
                _log.Warn("ChangeMateEquipment, Unknown mate!");
                return;
            }

            for (int i = 0; i < num; i++)
            {
                var itemTemplateId = stream.ReadUInt32();
                bool isEquip = itemTemplateId != 0;

                if (isEquip)
                    stream.Pos -= 4;

                var equipItem = new EquipItem();
                equipItem.Read(stream);

                if (isEquip)
                    stream.ReadUInt32(); //?

                var fromSlotType = (SlotType)stream.ReadByte();
                var fromSlot = stream.ReadByte();

                var equipSlotType = (SlotType)stream.ReadByte();
                var equipSlot = stream.ReadByte();

                _log.Debug("FROM: {0}, {1}; TO {2}, {3}, EQUIP: {4}", fromSlotType, fromSlot, equipSlotType, equipSlot, isEquip);

                var item = ItemManager.Instance.GetItemByItemId(equipItem.Id);
                var invItem = Connection.ActiveChar.Inventory.Bag.GetItemBySlot(fromSlot);
                var invItemId = invItem == null ? 0 : invItem.Id;

                if (item == null)
                {
                    _log.Debug("ChangeMateEquipment, failed to get item {0}", item.Id);
                    continue;
                }

                if (isEquip)
                {
                    if (mate.Equipment.AddOrMoveExistingItem(ItemTaskType.SwapItems, item))
                    {
                        Connection.SendPacket(new SCUnitEquipmentsChangedPacket(mate.ObjId, equipSlot, item));
                    }
                    //Connection.ActiveChar.Inventory.SplitOrMoveItemEx(ItemTaskType.SwapCofferItems, Connection.ActiveChar.Inventory.Bag, mate.Equipment, invItemId, fromSlotType, fromSlot, item.Id, equipSlotType, equipSlot);
                }
                else
                {
                    if (Connection.ActiveChar.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.SwapItems, item))
                    {
                        Connection.SendPacket(new SCUnitEquipmentsChangedPacket(mate.ObjId, equipSlot, null));
                    }
                    //Connection.ActiveChar.Inventory.SplitOrMoveItemEx(ItemTaskType.SwapItems, mate.Equipment, Connection.ActiveChar.Inventory.Bag, item.Id, equipSlotType, equipSlot, invItemId, fromSlotType, fromSlot);
                }
            }
        }
    }
}
