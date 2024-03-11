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
            var characterId = stream.ReadUInt32();
            var tl = stream.ReadUInt16(); // mate tl
            var passengerId = stream.ReadUInt32();
            var bts = stream.ReadBoolean();
            var num = stream.ReadByte();

            Logger.Debug($"ChangeMateEquipment, TlId: {tl}, Id: {characterId}, Id2: {passengerId}, BTS: {bts}, num: {num}");

            var mate = MateManager.Instance.GetActiveMateByTlId(tl);

            if (mate == null)
            {
                Logger.Warn($"ChangeMateEquipment, Unable to find mate with tlId {tl}!");
                return;
            }

            for (var i = 0; i < num; i++)
            {
                var invItem = new EquipItem();
                invItem.Read(stream);

                var equipItem = new EquipItem();
                equipItem.Read(stream);

                var invItemSlotType = (SlotType)stream.ReadByte();
                var invItemSlot = stream.ReadByte();

                var equipSlotType = (SlotType)stream.ReadByte();
                var equipSlot = stream.ReadByte();

                var isEquip = invItem.TemplateId != 0;

                invItem = (EquipItem)Connection.ActiveChar.Inventory.Bag.GetItemBySlot(invItemSlot);
                equipItem = (EquipItem)mate.Equipment.GetItemBySlot(equipSlot);

                Logger.Debug($"FROM: ({invItemSlotType}:{invItemSlot}) TO ({equipSlotType}:{equipSlot}) ITEMS: {invItem?.Id}, {equipItem?.Id}, EQUIP: {isEquip}");

                if (isEquip)
                {
                    if (invItem != null)
                    {
                        var itemTasks = new List<ItemTask>();
                        itemTasks.Add(new ItemRemove(invItem));
                        if (Connection.ActiveChar.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, Connection.ActiveChar.Inventory.Bag, mate.Equipment, invItem.Id, invItemSlotType, invItemSlot, 0, equipSlotType, equipSlot))
                        {
                            Connection.SendPacket(new SCMateEquipmentChangedPacket((invItemSlotType, invItemSlot, invItem), (equipSlotType, equipSlot, equipItem), tl, characterId, passengerId, bts, num));
                            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, itemTasks, new List<ulong>()));
                        }
                    }
                }
                else
                {
                    if (equipItem != null /* && Connection.ActiveChar.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, equipItem)*/)
                    {
                        if (Connection.ActiveChar.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, mate.Equipment, Connection.ActiveChar.Inventory.Bag, equipItem.Id, equipSlotType, equipSlot, 0, invItemSlotType, invItemSlot))
                        {
                            Connection.SendPacket(new SCMateEquipmentChangedPacket((invItemSlotType, invItemSlot, invItem), (equipSlotType, equipSlot, equipItem), tl, characterId, passengerId, bts, num));
                        }
                    }
                }
            }
        }
    }
}
