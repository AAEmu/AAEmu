using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellItemsPacket() : GamePacket(CSOffsets.CSSellItemsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var npcObjId = stream.ReadBc();
        var npc = Connection.ActiveChar.ParentWorld.GetNpc(npcObjId);
        if (npc == null || !npc.Template.Merchant)
            return;

        // A garden deed can move from Bag to the server-owned System container only through the
        // farmhand transaction. Keep the selected item references stable through sale and payment.
        using var inventoryMutation = Connection.ActiveChar.Inventory.AcquireMutation();

        var unkObjId = stream.ReadBc();

        var num = stream.ReadByte();
        var items = new List<Item>();
        var seenItems = new HashSet<ulong>();

        for (var i = 0; i < num; i++)
        {
            _ = stream.ReadByte();
            var slotType = (SlotType)stream.ReadByte();
            _ = stream.ReadByte();
            var slot = stream.ReadByte();

            var itemId = stream.ReadUInt64();
            var stack = stream.ReadUInt32();
            var removeReservationTime = stream.ReadUInt64();

            Item item = null;
            if (slotType == SlotType.Equipment)
                item = Connection.ActiveChar.Inventory.Equipment.GetItemBySlot(slot);
            else if (slotType == SlotType.Inventory)
                item = Connection.ActiveChar.Inventory.Bag.GetItemBySlot(slot);
            //                else if (slotType == SlotType.Bank)
            //                    item = Connection.ActiveChar.Inventory.Bank[slot];
            if (item != null && item.Id == itemId && stack <= int.MaxValue && (int)stack == item.Count && seenItems.Add(itemId))
            {
                Logger.Trace(
                    "SellItems item={0} slot={1}:{2} stack={3} removeReservationTime={4}",
                    itemId, slotType, slot, stack, removeReservationTime);
                items.Add(item);
            }
        }

        //var tasks = new List<ItemTask>();
        var saleLimits = ItemSaleLimitManager.Instance;
        var money = 0;
        foreach (var item in items)
        {
            // The sale columns of the template decide whether this vendor may take the item at all:
            // sellable, auction_only, and the per-day allowance of one_time_sale / limited_sale_count.
            // A refused item stays in the bag untouched - it is not moved to the buy-back list and it
            // pays nothing, so the two sides of the trade cannot drift apart.
            if (!saleLimits.TryConsumeSale(item.Template, out var decision))
            {
                Logger.Info("Vendor refused item {0} ({1}) from {2}: {3}",
                    item.Id, item.TemplateId, Connection.ActiveChar.Name, decision.Refusal);
                Connection.ActiveChar.SendErrorMessage(ToErrorMessage(decision.Refusal));
                continue;
            }

            if (!Connection.ActiveChar.BuyBackItems.AddOrMoveExistingItem(ItemTaskType.StoreSell, item))
            {
                Logger.Warn($"Failed to move sold itemId {item.Id} ({item.TemplateId}) to BuyBack ItemContainer for {Connection.ActiveChar.Name}");
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

    /// <summary>
    /// The message the client shows for each refusal. The client's own store text names the spent
    /// one-time sale and the auction-only item; a plain unsellable item gets the store's
    /// not-sellable text.
    /// </summary>
    private static ErrorMessageType ToErrorMessage(ItemSaleRefusal refusal) => refusal switch
    {
        ItemSaleRefusal.OneTimeSaleExhausted => ErrorMessageType.StoreOneTimeSale,
        ItemSaleRefusal.LimitedSaleExhausted => ErrorMessageType.StoreOneTimeSale,
        ItemSaleRefusal.AuctionOnly => ErrorMessageType.ItemAuctionOnly,
        _ => ErrorMessageType.StoreNotSellableItem
    };
}
