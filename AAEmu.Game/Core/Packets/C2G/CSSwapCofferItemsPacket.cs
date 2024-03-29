using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSwapCofferItemsPacket : GamePacket
{
    public CSSwapCofferItemsPacket() : base(CSOffsets.CSSwapCofferItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var fromItemId = stream.ReadUInt64(); // i1
        var toItemId = stream.ReadUInt64();   // i2

        var fromSlotType = (SlotType)stream.ReadByte(); // type
        var fromSlot = stream.ReadByte();          // index

        var toSlotType = (SlotType)stream.ReadByte(); // type
        var toSlot = stream.ReadByte();          // index

        var dbId = stream.ReadUInt64();         // dbDoodadId

        Logger.Debug($"SwapCofferItems, Item: {fromItemId} -> {toItemId}, SlotType: {fromSlotType} -> {toSlotType}, Slot: {fromSlot} -> {toSlot}, ItemContainerDbId: {dbId}");

        if (!Connection.ActiveChar.Inventory.SwapCofferItems(fromItemId, toItemId, fromSlotType, fromSlot, toSlotType, toSlot, dbId))
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.CannotMoveSoulboundItemToCoffer); // Not sure what error to send here
        }
    }
}
