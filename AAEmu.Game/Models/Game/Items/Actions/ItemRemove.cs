using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemRemove : ItemTask
{
    private readonly Item _item;
    private readonly ulong _Id;
    private readonly byte _slotType;
    private readonly byte _slot;
    private readonly uint _templateId;
    private readonly int _itemCount;
    private readonly DateTime _removeReservationTime;

    public ItemRemove(Item item)
    {
        _type = ItemAction.Remove; // 7
        _item = item;
        _removeReservationTime = DateTime.MinValue;
        _tLogt = SetTlogT(_type, item.SlotType, _itemCount < 0); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);   // type
        stream.Write((byte)_item.Slot);       // index
        stream.Write(_item.Id);               // id
        stream.Write(_item.Count);            // stack
        stream.Write(_removeReservationTime); // removeReservationTime
        stream.Write(0u);                     // type
        stream.Write(0u);                     // dbSlaveId
        stream.Write(_item.TemplateId);       // type

        return stream;
    }
}
