using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// CS_PACKET_ITEM_SECURE (0x07B) — "lock this item", sent from the bag's lock mode.
/// </summary>
/// <remarks>
/// The body is three fields: <c>type</c> (u8 container), <c>index</c> (u8 slot) and <c>itemId</c>
/// (u64), read straight off the client's own serializer. The previous parser here consumed four
/// separate bytes before the id, which left every field after the first one shifted.
/// </remarks>
public class CSItemSecurePacket() : GamePacket(CSOffsets.CSItemSecurePacket, 1)
{
    public SlotType SlotType { get; private set; }
    public byte Slot { get; private set; }
    public ulong ItemId { get; private set; }

    public override void Read(PacketStream stream)
    {
        SlotType = (SlotType)stream.ReadByte();
        Slot = stream.ReadByte();
        ItemId = stream.ReadUInt64();
    }

    public override void Execute()
    {
        Logger.Debug("ItemSecure, ItemId: {0}, SlotType: {1}, Slot: {2}", ItemId, SlotType, Slot);
        if (!ItemManager.Instance.SetItemSecurity(Connection.ActiveChar, SlotType, Slot, ItemId, true))
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ItemUpdateFail);
    }
}
