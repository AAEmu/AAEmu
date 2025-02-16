using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemUpdateBits : ItemTask
{
    private readonly Item _item;

    public ItemUpdateBits(Item item)
    {
        _type = ItemAction.SetFlagsBits; // 10
        _item = item;
        //_itemId = item.Id;
        //_slotType = item.SlotType;
        //_slot = (byte)item.Slot;
        //_bits = (byte)item.ItemFlags;
        // 10 image
        // 20 unwrapp
        //_oldBits = oldBits;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_item.SlotType);  // type
        stream.Write((byte)_item.Slot);      // index
        stream.Write(_item.Id);              // id
        stream.Write((byte)_item.ItemFlags); // bits
        stream.Write(DateTime.MinValue);     // soulBindChargeTime
        return stream;
    }
}
