using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSwapItemsPacket : GamePacket
    {
        public CSSwapItemsPacket() : base(CSOffsets.CSSwapItemsPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var fromItemId = stream.ReadUInt64(); // i1
            var toItemId = stream.ReadUInt64();   // i2

            var fromSlotType = (SlotType) stream.ReadByte(); // type
            var fromSlot = stream.ReadByte();           // index

            var toSlotType = (SlotType) stream.ReadByte();  // type
            var toSlot = stream.ReadByte();            // index

            Connection.ActiveChar.Inventory.SplitOrMoveItem(Models.Game.Items.Actions.ItemTaskType.SwapItems, fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
            // Connection.ActiveChar.Inventory.Move(fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
        }
    }
}
