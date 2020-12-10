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
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyItemsPacket : GamePacket
    {
        public CSBuyItemsPacket() : base(CSOffsets.CSBuyItemsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var npc = WorldManager.Instance.GetNpc(npcObjId);

            var doodadObjId = stream.ReadBc();
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);

            var unkId = stream.ReadUInt32(); // type(id)?

            var nBuy = stream.ReadByte();
            var nBuyBack = stream.ReadByte();

            _log.Debug("NPCObjId:{0} DoodadObjId:{1} unkId:{2} nBuy:{3} nBuyBack{4}", npcObjId, doodadObjId, unkId, nBuy, nBuyBack);

            // If a NPC was provided, check if it's valid
            if ((npcObjId != 0) && (npc == null || !npc.Template.Merchant || npc.Template.MerchantPackId == 0))
                return;
            MerchantGoods pack = null;
            if (npc != null)
            {
                var dist = MathUtil.CalculateDistance(Connection.ActiveChar.Position, npc.Position);
                if (dist > 3f) // 3m should be enough for NPC shops
                {
                    Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.TooFarAway);
                    return;
                }
                pack = NpcManager.Instance.GetGoods(npc.Template.MerchantPackId);
            }

            // If a Doodad was provided, check if we're near it
            if (doodadObjId != 0)
            {
                if (doodad == null)
                    return;
                var dist = MathUtil.CalculateDistance(Connection.ActiveChar.Position, doodad.Position);
                if (dist > 3f) // 3m should be enough for these
                {
                    Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.TooFarAway);
                    return;
                }
            }

            var money = 0;
            var honorPoints = 0;
            var vocationBadges = 0;

            // Get list of items to buy from the shop
            var itemsBuy = new List<(uint itemId, byte itemGrade, int itemCount)>();
            for (var i = 0; i < nBuy; i++)
            {
                var itemId = stream.ReadUInt32();
                var grade = stream.ReadByte();
                var count = stream.ReadInt32();
                var currency = (ShopCurrencyType)stream.ReadByte();

                // If using a NPC shop, check if the NPC is selling the specified item
                if ((npcObjId != 0) && ((pack == null) || (!pack.SellsItem(itemId))))
                    continue;

                if (doodadObjId != 0)
                {
                    // TODO: validate doodad "shop" (mirage furniture for example)
                    // unkId value looks related to the "shop type" for buying, but unsure how it's all linked
                }

                itemsBuy.Add((itemId, grade, count));
                var template = ItemManager.Instance.GetTemplate(itemId);

                if (currency == ShopCurrencyType.Money)
                    money += template.Price * count;
                else if (currency == ShopCurrencyType.Honor)
                    honorPoints += template.HonorPrice * count;
                else if (currency == ShopCurrencyType.VocationBadges)
                    vocationBadges += template.LivingPointPrice * count;
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
                honorPoints > Connection.ActiveChar.HonorPoint && 
                vocationBadges > Connection.ActiveChar.VocationPoint)
                return;

            var tasks = new List<ItemTask>();
            foreach (var (itemId, grade, count) in itemsBuy)
            {
                // Omit grade when creating to prevent "cheating" when creating the grade
                Connection.ActiveChar.Inventory.Bag.AcquireDefaultItem(ItemTaskType.StoreBuy, itemId, count, -1);
                // Connection.ActiveChar.Inventory.Bag.AcquireDefaultItem(ItemTaskType.StoreBuy, itemId, count, grade);
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

            if (honorPoints > 0)
            {
                Connection.ActiveChar.HonorPoint -= honorPoints;
                Connection.SendPacket(new SCGamePointChangedPacket(0, -honorPoints));
            }

            if (vocationBadges > 0)
            {
                Connection.ActiveChar.VocationPoint -= vocationBadges;
                Connection.SendPacket(new SCGamePointChangedPacket(1, -vocationBadges));
            }

            if (money > 0)
            {
                Connection.ActiveChar.ChangeMoney(SlotType.Inventory, -money);
            }

            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy, tasks, new List<ulong>()));
        }
    }
}
