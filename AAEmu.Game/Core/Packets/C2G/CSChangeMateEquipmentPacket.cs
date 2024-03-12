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

            if (num == 0)
                return;

            //                  SlotType, Slot, Item
            var invItems = new (SlotType, byte, Item)[num];
            var equipItems = new (SlotType, byte, Item)[num];
            var character = Connection.ActiveChar;

            for (var i = 0; i < num; i++)
            {
                invItems[i].Item3 = new EquipItem();
                invItems[i].Item3.Read(stream);

                equipItems[i].Item3 = new EquipItem();
                equipItems[i].Item3.Read(stream);

                invItems[i].Item1 = (SlotType)stream.ReadByte();
                invItems[i].Item2 = stream.ReadByte();

                equipItems[i].Item1 = (SlotType)stream.ReadByte();
                equipItems[i].Item2 = stream.ReadByte();

                var isEquip = invItems[i].Item3.TemplateId != 0;

                invItems[i].Item3 = (EquipItem)character.Inventory.Bag.GetItemBySlot(invItems[i].Item2);
                equipItems[i].Item3 = (EquipItem)mate.Equipment.GetItemBySlot(equipItems[i].Item2);

                Logger.Debug($"FROM: ({invItems[i].Item1}:{invItems[i].Item2}) TO ({equipItems[i].Item1}:{equipItems[i].Item2}) ITEMS: {invItems[i].Item3?.Id}, {equipItems[i].Item3?.Id}, EQUIP: {isEquip}");

                if (isEquip)
                {
                    if (invItems[i].Item3 != null)
                    {
                        var itemTasks = new List<ItemTask>();
                        itemTasks.Add(new ItemRemove(invItems[i].Item3));

                        if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, character.Inventory.Bag, mate.Equipment, invItems[i].Item3.Id, invItems[i].Item1, invItems[i].Item2, 0, equipItems[i].Item1, equipItems[i].Item2))
                        {
                            Connection.SendPacket(new SCMateEquipmentChangedPacket(invItems[i], equipItems[i], tl, characterId, passengerId, bts));
                            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, itemTasks, new List<ulong>()));
                        }
                    }
                }
                else
                {
                    if (equipItems[i].Item3 != null)
                    {
                        if (character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid, mate.Equipment, character.Inventory.Bag, equipItems[i].Item3.Id, equipItems[i].Item1, equipItems[i].Item2, 0, invItems[i].Item1, invItems[i].Item2))
                        {
                            Connection.SendPacket(new SCMateEquipmentChangedPacket(invItems[i], equipItems[i], tl, characterId, passengerId, bts));
                        }
                    }
                }
            }
        }
    }
}
