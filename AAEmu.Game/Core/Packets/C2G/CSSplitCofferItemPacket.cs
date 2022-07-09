using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSplitCofferItemPacket : GamePacket
    {
        public CSSplitCofferItemPacket() : base(CSOffsets.CSSplitCofferItemPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var count = stream.ReadInt32();
            var fromItemId = stream.ReadUInt64();
            var toItemId = stream.ReadUInt64();

            var fromSlotType = (SlotType)stream.ReadByte();
            var fromSlot = stream.ReadByte();

            var toSlotType = (SlotType)stream.ReadByte();
            var toSlot = stream.ReadByte();

            var dbId = stream.ReadUInt64();

            _log.Debug($"SplitCofferItem, Item: {count} x {fromItemId} -> {toItemId}, SlotType: {fromSlotType} -> {toSlotType}, Slot: {fromSlot} -> {toSlot}, ItemContainerDbId: {dbId}");

            if (!Connection.ActiveChar.Inventory.SplitCofferItems(count, fromItemId, toItemId, fromSlotType, fromSlot, toSlotType, toSlot, dbId))
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.CannotMoveSoulboundItemToCoffer); // Not sure what error to send here
            }
        }
    }
}
