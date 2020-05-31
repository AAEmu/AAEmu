using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyItemsPacket : GamePacket
    {
        public CSBuyItemsPacket() : base(0x0ae, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc == null || !npc.Template.Merchant || npc.Template.MerchantPackId == 0)
                return;

            var unkObjId = stream.ReadBc();
            var unkId = stream.ReadUInt32(); // type(id)

            var pack = NpcManager.Instance.GetGoods(npc.Template.MerchantPackId);
            if (pack == null)
                return;

            var nBuy = stream.ReadByte();
            var nBuyBack = stream.ReadByte();

            var money = 0;
            var honor = 0;
            var living = 0;

            // Get list of items to buy from the shop
            var itemsBuy = new List<(uint itemId, byte itemGrade, int itemCount)>();
            for (var i = 0; i < nBuy; i++)
            {
                var itemId = stream.ReadUInt32();
                var grade = stream.ReadByte();
                var count = stream.ReadInt32();
                var currency = (ShopCurrencyType)stream.ReadByte();

                if (!pack.Items.ContainsKey(itemId) || pack.Items[itemId].IndexOf(grade) < 0)
                    continue;

                itemsBuy.Add((itemId, grade, count));
                var template = ItemManager.Instance.GetTemplate(itemId);

                if (currency == ShopCurrencyType.Money)
                    money += template.Price * count;
                else if (currency == ShopCurrencyType.Honor)
                    honor += template.HonorPrice * count;
                else if (currency == ShopCurrencyType.VocationBadges)
                    living += template.LivingPointPrice * count;
                else
                {
                    _log.Error("Unknown currency type");
                }
            }

            // Get a list of items to buy from the buyback window
            var itemsBuyBack = new Dictionary<Item, int>();
            for (var i = 0; i < nBuyBack; i++)
            {
                var index = stream.ReadInt32();
                var item = Connection.ActiveChar.BuyBackItems.GetItemBySlot(index);
                /*
                if (index >= Connection.ActiveChar.BuyBack.Length)
                    continue;

                var item = Connection.ActiveChar.BuyBack[index];
                */
                if (item == null)
                    continue;
                itemsBuyBack.Add(item, index);
                money += (int)(item.Template.Refund * ItemManager.Instance.GetGradeTemplate(item.Grade).RefundMultiplier / 100f) *
                         item.Count;
            }

            var useAAPoint = stream.ReadBoolean();

            if (money > Connection.ActiveChar.Money && 
                honor > Connection.ActiveChar.HonorPoint && 
                living > Connection.ActiveChar.VocationPoint)
                return;

            var tasks = new List<ItemTask>();
            foreach (var (itemId, grade, count) in itemsBuy)
            {
                Connection.ActiveChar.Inventory.Bag.AcquireDefaultItem(ItemTaskType.StoreBuy, itemId, count, grade);
                /*
                var item = ItemManager.Instance.Create(itemId, count, grade);
                if (item == null)
                    return;
                var res = Connection.ActiveChar.Inventory.AddItem(ItemTaskType.StoreBuy, item);
                if (res == null)
                {
                    ItemManager.Instance.ReleaseId(item.Id);
                    return;
                }

                if (res.Id != item.Id)
                    tasks.Add(new ItemCountUpdate(res, item.Count));
                else
                    tasks.Add(new ItemAdd(item));
                */
            }

            foreach (var (item, index) in itemsBuyBack)
            {
                Connection.ActiveChar.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.StoreBuy, item);
                tasks.Add(new ItemBuyback(item));
                /*
                var res = Connection.ActiveChar.Inventory.AddItem(ItemTaskType.StoreBuy, item);
                if (res == null)
                {
                    ItemManager.Instance.ReleaseId(item.Id);
                    return;
                }

                if (res.Id != item.Id)
                    tasks.Add(new ItemCountUpdate(res, item.Count));
                else
                    tasks.Add(new ItemBuyback(item));
                Connection.ActiveChar.BuyBack[index] = null;
                */
            }

            if (honor > 0)
            {
                Connection.ActiveChar.HonorPoint -= honor;
                Connection.SendPacket(new SCGamePointChangedPacket(0, -honor));
            }

            if (living > 0)
            {
                Connection.ActiveChar.VocationPoint -= living;
                Connection.SendPacket(new SCGamePointChangedPacket(1, -living));
            }

            if (money > 0)
            {
                Connection.ActiveChar.ChangeMoney(SlotType.Inventory, -money);
                /*
                Connection.ActiveChar.Money -= money;
                tasks.Add(new MoneyChange(-money));
                */
            }

            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy, tasks, new List<ulong>()));
        }
    }
}
