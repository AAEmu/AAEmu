using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSellItemsPacket : GamePacket
    {
        public CSSellItemsPacket() : base(0x0b0, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc == null || !npc.Template.Merchant)
                return;

            var unkObjId = stream.ReadBc();

            var num = stream.ReadByte();
            var items = new List<Item>();

            for (var i = 0; i < num; i++)
            {
                var slotType = (SlotType)stream.ReadByte();
                var slot = stream.ReadByte();

                var itemId = stream.ReadUInt64();
                var unkId = stream.ReadUInt32();

                Item item = null;
                if (slotType == SlotType.Equipment)
                    item = Connection.ActiveChar.Inventory.Equip[slot];
                else if (slotType == SlotType.Inventory)
                    item = Connection.ActiveChar.Inventory.Items[slot];
//                else if (slotType == SlotType.Bank)
//                    item = Connection.ActiveChar.Inventory.Bank[slot];
                if (item != null && item.Id == itemId)
                    items.Add(item);
            }

            var tasks = new List<ItemTask>();
            var money = 0;
            foreach (var item in items)
            {
                if (!item.Template.Sellable)
                    continue;
                Connection.ActiveChar.Inventory.RemoveItem(item, false);
                var res = false;
                for (var i = 0; i < 20; i++)
                {
                    if (Connection.ActiveChar.BuyBack[i] == null)
                    {
                        Connection.ActiveChar.BuyBack[i] = item;
                        res = true;
                        break;
                    }
                }

                if (!res)
                {
                    ItemIdManager.Instance.ReleaseId((uint)Connection.ActiveChar.BuyBack[0].Id);
                    var temp = new Item[20];
                    Array.Copy(Connection.ActiveChar.BuyBack, 1, temp, 0, 19);
                    temp[19] = item;
                    Connection.ActiveChar.BuyBack = temp;
                }

                tasks.Add(new ItemRemove(item));
                money += (int)(item.Template.Refund * ItemManager.Instance.GetGradeTemplate(item.Grade).RefundMultiplier / 100f) *
                         item.Count;
            }

            Connection.ActiveChar.Money += money;
            tasks.Add(new MoneyChange(money));
            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreSell, tasks, new List<ulong>()));
        }
    }
}
