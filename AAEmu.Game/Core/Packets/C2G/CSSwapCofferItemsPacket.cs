using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSwapCofferItemsPacket : GamePacket
    {
        public CSSwapCofferItemsPacket() : base(CSOffsets.CSSwapCofferItemsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var fromItemId = stream.ReadUInt64();
            var toItemId = stream.ReadUInt64();

            stream.ReadByte();
            var fromSlotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var fromSlot = stream.ReadByte();

            stream.ReadByte();
            var toSlotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var toSlot = stream.ReadByte();

            var dbDoodadId = stream.ReadInt64();

            _log.Debug(
                "SwapCofferItems, Item: {0} -> {1}, SlotType: {2} -> {3}, Slot: {4} -> {5}",
                fromItemId, toItemId, fromSlotType, toSlotType, fromSlot, toSlot);
        }
    }
}
