using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Chat;
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

        var inventory = Connection.ActiveChar.Inventory;

        // Destroying is one of the three actions an account-protection window covers. The guard is inert
        // unless feature bit 56 is on, so this is a no-op on a default server.
        if (!SensitiveOperationGuard.MayPerform(Connection.ActiveChar,
                Models.Game.SensitiveOperation.SensitiveOperationKind.ItemDestruction, out var protectionReason))
        {
            Connection.ActiveChar.SendMessage(ChatType.System, protectionReason);
            return;
        }

        Item item;
        using (inventory.AcquireMutation())
        {
            // Prefer the slot the client pointed at, but fall back to the id so a stale client-side
            // slot doesn't make the destroy silently fail. The id check below still guards both paths.
            item = inventory.GetItem(slotType, slot)
                   ?? inventory.GetItemById(itemId);

            if (!IsValidDestroyTarget(item, itemId, slotType, amount))
            {
                Logger.Warn($"DestroyItem: Invalid item, itemId {itemId}, slotType {slotType}, slot {slot}, amount {amount}, found {(item == null ? "none" : $"id {item.Id} count {item.Count}")}");
                return;
            }

            var count = checked((int)amount);
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
            }
        }

        inventory.OnItemManuallyDestroyed(item, checked((int)amount));
    }

    internal static bool IsValidDestroyTarget(Item item, ulong itemId, SlotType requestedSlotType, uint amount) =>
        item != null && item.Id == itemId && amount is > 0 and <= int.MaxValue && item.Count > 0 &&
        amount <= (uint)item.Count &&
        requestedSlotType != SlotType.System && item.SlotType != SlotType.System &&
        item._holdingContainer?.ContainerType != SlotType.System &&
        !AuctionHouseRules.IsEscrowSlot(item.SlotType) && !AuctionHouseRules.IsEscrowSlot(requestedSlotType);
}
