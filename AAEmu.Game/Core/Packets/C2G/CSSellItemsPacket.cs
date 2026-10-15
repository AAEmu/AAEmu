using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
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
        {
            Logger.Debug(
                "SellItems refused: npcObjId={0} missingOrNotMerchant for {1}",
                npcObjId, Connection.ActiveChar.Name);
            return;
        }

        // A garden deed can move from Bag to the server-owned System container only through the
        // farmhand transaction. Keep the selected item references stable through sale and payment.
        using var inventoryMutation = Connection.ActiveChar.Inventory.AcquireMutation();

        // Second bc is the doodad store (0 when the seller is an NPC). Buy uses the same pair.
        _ = stream.ReadBc();

        var num = stream.ReadByte();
        if (num > VendorSellWireRules.BuybackListCapacity)
            return;

        var items = new List<Item>(num);
        var seenItems = new HashSet<ulong>();

        for (var i = 0; i < num; i++)
        {
            // Client sell-entry: u8 type, u8 index, u64 id, u32 stack, u64 removeReservationTime,
            // u32 type, u32 dbSlaveId, u32 type. The padded 4-byte slot layout is in-memory only.
            var slotType = (SlotType)stream.ReadByte();
            var slot = stream.ReadByte();
            var itemId = stream.ReadUInt64();
            var stack = stream.ReadUInt32();
            var removeReservationTime = stream.ReadUInt64();
            _ = stream.ReadUInt32(); // detail/type
            _ = stream.ReadUInt32(); // dbSlaveId
            _ = stream.ReadUInt32(); // owner/type

            if (!VendorSellWireRules.IsSellableContainer(slotType))
                continue;

            Item item = null;
            if (slotType == SlotType.Equipment)
                item = Connection.ActiveChar.Inventory.Equipment.GetItemBySlot(slot);
            else if (slotType == SlotType.Inventory)
                item = Connection.ActiveChar.Inventory.Bag.GetItemBySlot(slot);

            // Wire "stack" is unused on this client (always 0); sold qty is item.Count below.
            if (item == null || item.Id != itemId ||
                !VendorSellWireRules.StackFieldAllowsSale(stack, item.Count) ||
                !seenItems.Add(itemId))
            {
                Logger.Debug(
                    "SellItems skip itemId={0} slot={1}:{2} wireStack={3} bagCount={4} found={5} for {6}",
                    itemId, slotType, slot, stack, item?.Count, item != null, Connection.ActiveChar.Name);
                continue;
            }

            Logger.Debug(
                "SellItems item={0} slot={1}:{2} stack={3} removeReservationTime={4}",
                itemId, slotType, slot, stack, removeReservationTime);
            items.Add(item);
        }

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

        TrimBuybackList(Connection.ActiveChar);

        var payout = VendorSalePayoutRules.BatchValue(lines);
        if (payout > 0)
        {
            // Pay in its own StoreSell task packet. Combining money with the bag remove (as the old
            // commented path did) leaves the client ignoring the gold half after a remove body.
            Connection.ActiveChar.ChangeMoney(SlotType.Inventory, payout, ItemTaskType.StoreSell);
        }

        // Refresh the buy-back tab for the open store window. Sold items stay here until logout
        // (EnterWorld wipe) or until they fall off the capacity cap above — that is the repurchase
        // window the client offers after a sale.
        Connection.ActiveChar.BuyBackItems.ReNumberSlots();
        Connection.SendPacket(new SCSoldItemListPacket(Connection.ActiveChar.BuyBackItems.Items));
    }

    /// <summary>
    /// Drops the oldest buy-back rows past the sold-item list cap. Newest sales are inserted at the
    /// front of <see cref="Character.BuyBackItems"/>, so excess lives at the tail.
    /// </summary>
    private static void TrimBuybackList(Character character)
    {
        var buyback = character.BuyBackItems;
        var excess = VendorSellWireRules.ExcessBuybackCount(buyback.Items.Count);
        for (var i = 0; i < excess; i++)
        {
            var oldest = buyback.Items[^1];
            buyback.RemoveItem(ItemTaskType.Invalid, oldest, true);
        }
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
