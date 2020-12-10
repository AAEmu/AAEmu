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
        public CSSellItemsPacket() : base(CSOffsets.CSSellItemsPacket, 1)
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
                    item = Connection.ActiveChar.Inventory.Equipment.GetItemBySlot(slot);
                else if (slotType == SlotType.Inventory)
                    item = Connection.ActiveChar.Inventory.Bag.GetItemBySlot(slot);
//                else if (slotType == SlotType.Bank)
//                    item = Connection.ActiveChar.Inventory.Bank[slot];
                if (item != null && item.Id == itemId)
                    items.Add(item);
            }

            //var tasks = new List<ItemTask>();
            var money = 0;
            foreach (var item in items)
            {
                if (!item.Template.Sellable)
                    continue;

                if (!Connection.ActiveChar.BuyBackItems.AddOrMoveExistingItem(ItemTaskType.StoreSell, item))
                {
                    _log.Warn(string.Format("Failed to move sold itemId {0} to BuyBack ItemContainer for {1}",item.Id,Connection.ActiveChar.Name));
                }
                money += (int)(item.Template.Refund * ItemManager.Instance.GetGradeTemplate(item.Grade).RefundMultiplier / 100f) *
                         item.Count;
            }

            Connection.ActiveChar.ChangeMoney(SlotType.Inventory, money);
            /*
            Connection.ActiveChar.Money += money;
            tasks.Add(new MoneyChange(money));
            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreSell, itemTasks, new List<ulong>()));
            */
        }
    }
}
