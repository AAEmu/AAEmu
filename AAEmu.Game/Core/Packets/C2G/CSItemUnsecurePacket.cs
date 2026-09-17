using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// CS_PACKET_ITEM_UNSECURE (0x07C) — "start unlocking this item", sent from the bag's unlock mode.
/// </summary>
/// <remarks>
/// Same three fields as <see cref="CSItemSecurePacket"/> (<c>type</c> u8, <c>index</c> u8,
/// <c>itemId</c> u64). Unlocking is not instant: the item starts the delay the client itself
/// advertises through <c>X2Item:GetSecurityUnlockDelayTime()</c>.
/// </remarks>
public class CSItemUnsecurePacket() : GamePacket(CSOffsets.CSItemUnsecurePacket, 1)
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
        Logger.Debug("ItemUnsecure, ItemId: {0}, SlotType: {1}, Slot: {2}", ItemId, SlotType, Slot);
        if (!ItemManager.Instance.SetItemSecurity(Connection.ActiveChar, SlotType, Slot, ItemId, false))
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ItemUpdateFail);
    }
}
