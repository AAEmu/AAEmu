using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSplitBagItemPacket : GamePacket
    {
        public CSSplitBagItemPacket() : base(0x037, 1)
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

            var count = stream.ReadInt32();

            Connection.ActiveChar.Inventory.Move(fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot, count);
        }
    }
}