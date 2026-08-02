using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// (u8 owner type, i64 owner id), bool open, u8 count, then u8 slot index plus item payload.
/// </remarks>
public class SCCofferContentsUpdatePacket : GamePacket
{
    public const byte MaxSlotsToSend = 30;
    private readonly DoodadCoffer _cofferDoodad;
    private readonly CofferOwnerType _ownerType;
    private readonly long _ownerId;
    private readonly ItemContainer _itemContainer;
    private readonly bool _isOpened;
    private readonly byte _firstSlot;
    private readonly byte _slotCount;

    public SCCofferContentsUpdatePacket(DoodadCoffer cofferDoodad, byte firstSlot, bool isOpened = true)
        : this(cofferDoodad, CofferOwnerType.Coffer,
            unchecked((long)(cofferDoodad?.GetItemContainerId() ?? 0)), cofferDoodad?.ItemContainer,
            firstSlot, isOpened)
    {
    }

    public SCCofferContentsUpdatePacket(DoodadCoffer cofferDoodad, ItemBag itemBag,
        ItemBagContainer itemContainer, byte firstSlot, bool isOpened = true)
        : this(cofferDoodad, CofferOwnerType.ItemBag, unchecked((long)(itemBag?.Id ?? 0)), itemContainer,
            firstSlot, isOpened)
    {
    }

    private SCCofferContentsUpdatePacket(DoodadCoffer cofferDoodad, CofferOwnerType ownerType,
        long ownerId, ItemContainer itemContainer, byte firstSlot, bool isOpened)
        : base(SCOffsets.SCCofferContentsUpdatePacket, 1)
    {
        _cofferDoodad = cofferDoodad;
        _ownerType = ownerType;
        _ownerId = ownerId;
        _itemContainer = itemContainer;
        _isOpened = isOpened;
        _firstSlot = firstSlot;
        var lastSlot = _firstSlot + MaxSlotsToSend;
        if (lastSlot >= (_itemContainer?.ContainerSize ?? 0))
            lastSlot = _itemContainer?.ContainerSize ?? 0;
        var slotCount = lastSlot - _firstSlot;
        if (slotCount >= MaxSlotsToSend)
            slotCount = MaxSlotsToSend;
        _slotCount = (byte)slotCount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_itemContainer == null)
            Logger.Warn($"No ItemContainer assigned to Coffer, objId: {_cofferDoodad?.ObjId} dbId: {_cofferDoodad?.DbId}");

        stream.WriteBc(_cofferDoodad?.ObjId ?? 0);
        stream.Write((byte)_ownerType);
        stream.Write(_ownerId);
        stream.Write(_isOpened);
        stream.Write(_slotCount);
        for (byte i = 0; i < _slotCount; i++)
        {
            var slot = (byte)(_firstSlot + i);
            stream.Write(slot);
            var item = _itemContainer?.GetItemBySlot(slot);
            if (item == null)
                stream.Write(0u); // uint
            else
                stream.Write(item);
        }

        return stream;
    }
}
