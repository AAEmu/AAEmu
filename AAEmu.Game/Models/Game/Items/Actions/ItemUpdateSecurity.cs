using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemUpdateSecurity : ItemTask
{
    private readonly Item _item;
    private readonly byte _bits;
    private readonly bool _isUnsecureExcess;
    private readonly bool _isUnsecureSet;
    private readonly bool _isUnpack;

    /// <summary>Flags the item carried before this change; the client tracks it as "prevBits".</summary>
    private readonly byte _prevBits;

    /// <summary>Owner of the action — 0 is the acting character, matching the packet-level default.</summary>
    private readonly byte _actionOwnerType;

    public ItemUpdateSecurity(Item item, byte bits, bool isUnsecureExcess, bool isUnsecureSet, bool isUnpack,
        byte prevBits = 0, byte actionOwnerType = 0)
    {
        _actionOwnerType = actionOwnerType;
        _item = item;
        _bits = bits;
        _isUnsecureExcess = isUnsecureExcess;
        _isUnsecureSet = isUnsecureSet;
        _isUnpack = isUnpack;
        _prevBits = prevBits;
        _type = ItemAction.UpdateFlags;
    }

    /// <summary>
    /// index u8, itemId i64, bits u8, prevBits u8, isUnsecureExcess/isUnSecureSet/isUnpack bool,
    /// unSecureDateTime i64, unpackDateTime i64. Omitting actionOwnerType and prevBits shifted every
    /// field after them, so an unwrap arrived with its flag and timestamps read out of the wrong bytes.
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_actionOwnerType);
        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);
        stream.Write(_item.Id);
        stream.Write(_bits);
        stream.Write(_prevBits);
        stream.Write(_isUnsecureExcess);
        stream.Write(_isUnsecureSet);
        stream.Write(_isUnpack);
        stream.Write(_item.UnsecureTime);
        stream.Write(_item.UnpackTime);
        return stream;
    }
}
