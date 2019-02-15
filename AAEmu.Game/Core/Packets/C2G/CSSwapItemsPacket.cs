using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSwapItemsPacket : GamePacket
    {
        public CSSwapItemsPacket() : base(0x03a, 1) // TODO 1.0 opcode: 0x038
        {
        }

        public override void Read(PacketStream stream)
        {
            var fromItemId = stream.ReadUInt64();
            var toItemId = stream.ReadUInt64();

            stream.ReadByte();
            var fromSlotType = (SlotType) stream.ReadByte();
            stream.ReadByte();
            var fromSlot = stream.ReadByte();

            stream.ReadByte();
            var toSlotType = (SlotType) stream.ReadByte();
            stream.ReadByte();
            var toSlot = stream.ReadByte();

            Connection.ActiveChar.Inventory.Move(fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
        }
    }
}
