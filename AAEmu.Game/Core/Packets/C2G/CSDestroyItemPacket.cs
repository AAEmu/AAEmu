using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDestroyItemPacket() : GamePacket(CSOffsets.CSDestroyItemPacket, 1)
{
    //

    public override void Read(PacketStream stream)
    {
        // Body is 14 bytes: itemId (u64), slotType (u8), slot (u8), amount (u32).
        // There is no actionOwnerType/padding here — that only exists in the S2C ItemTask bodies.
        var itemId = stream.ReadUInt64();
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var amount = stream.ReadUInt32();

        // Prefer the slot the client pointed at, but fall back to the id so a stale client-side
        // slot doesn't make the destroy silently fail. The id check below still guards both paths.
        var item = Connection.ActiveChar.Inventory.GetItem(slotType, slot)
                   ?? Connection.ActiveChar.Inventory.GetItemById(itemId);

        if (item == null || item.Id != itemId || amount == 0 || amount > int.MaxValue || (int)amount > item.Count
            || AuctionHouseRules.IsEscrowSlot(item.SlotType) || AuctionHouseRules.IsEscrowSlot(slotType))
        {
            Logger.Warn($"DestroyItem: Invalid item, itemId {itemId}, slotType {slotType}, slot {slot}, amount {amount}, found {(item == null ? "none" : $"id {item.Id} count {item.Count}")}");
            return;
        }

        var count = (int)amount;

        if (item.Count > count)
        {
            item.Count -= count;
            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, [new ItemCountUpdate(item, -count)], []));
        }
        else
        {
            // Sanity check in case we're destroying something we're not actually holding?
            if (item._holdingContainer == null)
            {
                ItemManager.Instance.ReleaseId(item.Id);
                Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, [new ItemRemove(item)], []));
            }
            else
            if (!item._holdingContainer.RemoveItem(ItemTaskType.Destroy, item, true))
            {
                Logger.Warn("DestroyItem: Failed to destroy item...");
                return;
            }
            // Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, new List<ItemTask> { new ItemRemove(item) }, new List<ulong>()));
        }

        Connection.ActiveChar?.Inventory.OnItemManuallyDestroyed(item, count);
    }
}
