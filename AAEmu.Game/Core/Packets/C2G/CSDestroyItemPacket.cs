using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDestroyItemPacket : GamePacket 
    {
        public CSDestroyItemPacket() : base(CSOffsets.CSDestroyItemPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var itemId = stream.ReadUInt64();
            var slotType = (SlotType) stream.ReadByte();
            var slot = stream.ReadByte();
            var count = stream.ReadInt32();

            var item = Connection.ActiveChar.Inventory.GetItem(slotType, slot);
            if (item == null || item.Id != itemId || item.Count < count)
            {
                _log.Warn("DestroyItem: Invalid item...");
                // TODO ... ItemNotify?
                return;
            }

            if (item.Count > count)
            {
                item.Count -= count;
                Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, new List<ItemTask> { new ItemCountUpdate(item, -count) }, new List<ulong>()));
            }
            else
            {
                // Sanity check in case we're destroying something we're not actually holding?
                if (item._holdingContainer == null)
                {
                    ItemManager.Instance.ReleaseId(item.Id);
                    Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, new List<ItemTask> { new ItemRemove(item) }, new List<ulong>()));
                }
                else
                if (!item._holdingContainer.RemoveItem(ItemTaskType.Destroy, item, true))
                {
                    _log.Warn("DestroyItem: Failed to destroy item...");
                }
                // Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Destroy, new List<ItemTask> { new ItemRemove(item) }, new List<ulong>()));
            }
        }
    }
}
