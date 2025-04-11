using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDestroyItemPacket : GamePacket
{
    public CSDestroyItemPacket() : base(CSOffsets.CSDestroyItemPacket, 1)
    {
        //
    }

    public override void Read(PacketStream stream)
    {
        var itemId = stream.ReadUInt64();
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var count = stream.ReadInt32();

        var item = Connection.ActiveChar.Inventory.GetItem(slotType, slot);
        if (item == null || item.Id != itemId || item.Count < count)
        {
            Logger.Warn("DestroyItem: Invalid item...");
            // TODO ... ItemNotify?
            return;
        }

        if (count <= 0)
        {
            // The amount to destroy should always be more than 0, assume hacking otherwise, and just destroy the entire item
            SusManager.Instance.LogActivity(
                SusManager.CategoryCheating,
                Connection.ActiveChar,
                $"CSDestroyItemPacket, player {Connection.ActiveChar?.Name} attempted to destroy a negative amount of items {count} for item: template {item.TemplateId}, id {item.Id}");
            count = item.Count;
        }

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

        Connection.ActiveChar?.Inventory.OnItemManuallyDestroyed(item, item.Count);
    }
}
