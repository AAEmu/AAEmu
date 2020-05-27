using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSwapItemsPacket : GamePacket
    {
        public CSSwapItemsPacket() : base(0x03a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var fromItemId = stream.ReadUInt64();
            var toItemId = stream.ReadUInt64();

            var fromSlotType = (SlotType) stream.ReadByte();
            var fromSlot = stream.ReadByte();

            var toSlotType = (SlotType) stream.ReadByte();
            var toSlot = stream.ReadByte();

            DbLoggerCategory.Database.Connection.ActiveChar.Inventory.SplitOrMoveItem(Models.Game.Items.Actions.ItemTaskType.SwapItems, fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
            // Connection.ActiveChar.Inventory.Move(fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
        }
    }
}
