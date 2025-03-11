using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBuyItemsPacket : GamePacket
{
    public CSBuyItemsPacket() : base(CSOffsets.CSBuyItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        List<Merchants> pack = null;

        var npcObjId = stream.ReadBc();

        var npc = WorldManager.Instance.GetNpc(npcObjId);
        // If a NPC was provided, check if it's valid
        if (npc != null)
        {
            if (npcObjId != 0 && npc.Template.Merchant)
            {
                var dist = MathUtil.CalculateDistance(Connection.ActiveChar.Transform.World.Position, npc.Transform.World.Position);
                if (dist > 3f) // 3m should be enough for NPC shops
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.TooFarAway);
                    return;
                }

                pack = NpcManager.Instance.GetMerchantGoods(npc.Template.Id);
            }
        }

        var doodadObjId = stream.ReadBc();

        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
        // If a Doodad was provided, check if we're near it
        if (doodadObjId != 0)
        {
            if (doodad == null)
            {
                return;
            }

            var dist = MathUtil.CalculateDistance(Connection.ActiveChar.Transform.World.Position, doodad.Transform.World.Position);
            if (dist > 3f) // 3m should be enough for these
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.TooFarAway);
                return;
            }
        }

        var unkId = stream.ReadUInt32(); // type(id)?
        var nBuy = stream.ReadByte();
        var nBuyBack = stream.ReadByte();

        Logger.Debug($"NPCObjId:{npcObjId}, DoodadObjId:{doodadObjId}, unkId:{unkId}, nBuy:{nBuy}, nBuyBack{nBuyBack}");

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
            if (npcObjId != 0 && (pack == null || !Merchants.SellsItem(itemId, pack)))
            {
                continue;
            }

            if (doodadObjId != 0)
            {
                // TODO: validate doodad "shop" (mirage furniture for example)
                // unkId value looks related to the "shop type" for buying, but unsure how it's all linked
            }

            itemsBuy.Add((itemId, grade, count));
            var template = ItemManager.Instance.GetTemplate(itemId);

            if (currency == ShopCurrencyType.Money)
            {
                money += template.Price * count;
            }
            else if (currency == ShopCurrencyType.Honor)
            {
                honorPoints += template.HonorPrice * count;
            }
            else if (currency == ShopCurrencyType.VocationBadges)
            {
                vocationBadges += template.LivingPointPrice * count;
            }
            else
            {
                Logger.Error("Unknown currency type");
            }
        }

        // Get a list of items to buy from the buyback window
        var itemsBuyBack = new Dictionary<Item, int>();
        for (var i = 0; i < nBuyBack; i++)
        {
            var index = stream.ReadInt32();
            var item = Connection.ActiveChar.BuyBackItems.GetItemBySlot(index);
            if (item == null)
            {
                continue;
            }

            itemsBuyBack.Add(item, index);
            money += (int)(item.Template.Refund * ItemManager.Instance.GetGradeTemplate(item.Grade).RefundMultiplier / 100f) * item.Count;
        }

        var useAAPoint = stream.ReadBoolean();
        var openType = stream.ReadByte();

        if (money > Connection.ActiveChar.Money && honorPoints > Connection.ActiveChar.HonorPoint && vocationBadges > Connection.ActiveChar.VocationPoint)
        {
            return;
        }

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
        }

        if (honorPoints > 0)
        {
            Connection.ActiveChar.ChangeGamePoints(GamePointKind.Honor, -honorPoints);
        }

        if (vocationBadges > 0)
        {
            Connection.ActiveChar.ChangeGamePoints(GamePointKind.Vocation, -vocationBadges);
        }

        if (money > 0)
        {
            //Connection.ActiveChar.ChangeMoney(SlotType.Bag, -money);
            tasks.Add(new MoneyChange(money));
        }

        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy, tasks, new List<ulong>()));
    }
}
