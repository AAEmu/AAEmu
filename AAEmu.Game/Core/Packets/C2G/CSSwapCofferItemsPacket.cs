using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// u8 index) pairs, then the shared coffer owner descriptor (u8 owner type, i64 owner id).
/// </remarks>
/// <remarks>
/// u8 index) pairs, then the shared coffer owner descriptor (u8 owner type, i64 owner id).
/// </remarks>
/// <remarks>
/// u8 index) pairs, then the shared coffer owner descriptor (u8 owner type, i64 owner id).
/// </remarks>
public class CSSwapCofferItemsPacket() : GamePacket(CSOffsets.CSSwapCofferItemsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var fromItemId = stream.ReadInt64();
        var toItemId = stream.ReadInt64();

        var fromSlotType = (SlotType)stream.ReadByte();
        var fromSlot = stream.ReadByte();

        var toSlotType = (SlotType)stream.ReadByte();
        var toSlot = stream.ReadByte();

        var ownerType = (CofferOwnerType)stream.ReadByte();
        var ownerId = stream.ReadInt64();

        Logger.Debug($"SwapCofferItems, Item: {fromItemId} -> {toItemId}, SlotType: {fromSlotType} -> {toSlotType}, Slot: {fromSlot} -> {toSlot}, Owner: {ownerType}:{ownerId}");

        if (fromItemId < 0 || toItemId < 0 || ownerId <= 0 ||
            !Connection.ActiveChar.Inventory.SwapCofferItems((ulong)fromItemId, (ulong)toItemId, fromSlotType,
                fromSlot, toSlotType, toSlot, ownerType, (ulong)ownerId))
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.CannotMoveSoulboundItemToCoffer); // Not sure what error to send here
        }
    }
}
