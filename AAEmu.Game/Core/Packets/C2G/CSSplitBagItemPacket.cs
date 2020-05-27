using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSplitBagItemPacket : GamePacket
    {
        public CSSplitBagItemPacket() : base(0x039, 1)
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

            var count = stream.ReadInt32();

            DbLoggerCategory.Database.Connection.ActiveChar.Inventory.SplitOrMoveItem(ItemTaskType.SwapItems, fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot, count);
            //Connection.ActiveChar.Inventory.Move(fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot, count);
        }
    }
}
