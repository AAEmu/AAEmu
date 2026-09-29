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
        // Money is accumulated only for items that actually reached the buy-back list, and it is paid
        // once after the loop. A refused or unmoved item is never added to the total, so the two
        // sides of the trade cannot drift apart: there is no path that pays for an item the character
        // does not hold, and none that moves an item without paying for it. Nothing inside the loop
        // may throw past the payment, because an exception here would leave already-moved items
        // sitting in buy-back unpaid.
        var lines = new List<(int Refund, float Multiplier, int Count, ItemSaleTransfer Transfer)>(items.Count);
        foreach (var item in items)
        {
            // The sale columns of the template decide whether this vendor may take the item at all:
            // sellable and auction_only. A refused item stays in the bag untouched.
            var decision = ItemSaleRules.Evaluate(item.Template);
            if (!decision.Allowed)
            {
                Logger.Info("Vendor refused item {0} ({1}) from {2}: {3}",
                    item.Id, item.TemplateId, Connection.ActiveChar.Name, decision.Refusal);
                Connection.ActiveChar.SendErrorMessage(ToErrorMessage(decision.Refusal));
                continue;
            }

            // The line is recorded with the outcome that actually happened, so the payout is computed
            // from what moved rather than from what was attempted. Nothing inside this loop may throw
            // past the payment below: an exception here would leave already-moved items in buy-back
            // unpaid.
            var moved = Connection.ActiveChar.BuyBackItems.AddOrMoveExistingItem(ItemTaskType.StoreSell, item);
            if (!moved)
            {
                // The item is still in the bag, so it must not be paid for. Carrying on rather than
                // throwing is deliberate: the remaining items are unrelated to this one, and the
                // payment covers exactly the lines that moved.
                Logger.Warn("Failed to move sold itemId {0} ({1}) to BuyBack ItemContainer for {2}; "
                            + "it stays in the bag and pays nothing.",
                    item.Id, item.TemplateId, Connection.ActiveChar.Name);
            }

            lines.Add((
                item.Template.Refund,
                ItemManager.Instance.GetGradeTemplate(item.Grade).RefundMultiplier,
                item.Count,
                moved ? ItemSaleTransfer.Moved : ItemSaleTransfer.NotMoved));
        }

        Connection.ActiveChar.ChangeMoney(
            SlotType.Inventory, VendorSalePayoutRules.BatchValue(lines));
        /*
        Connection.ActiveChar.Money += money;
        tasks.Add(new MoneyChange(money));
        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreSell, itemTasks, new List<ulong>()));
        */
    }

    /// <summary>
    /// The message the client shows for each refusal. The client's own store text names the
    /// auction-only item; a plain unsellable item gets the store's not-sellable text.
    /// </summary>
    private static ErrorMessageType ToErrorMessage(ItemSaleRefusal refusal) => refusal switch
    {
        ItemSaleRefusal.AuctionOnly => ErrorMessageType.ItemAuctionOnly,
        _ => ErrorMessageType.StoreNotSellableItem
    };
}
