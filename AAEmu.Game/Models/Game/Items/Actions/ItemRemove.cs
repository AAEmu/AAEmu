using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemRemove : ItemTask
{
    private readonly ulong _itemId;
    private readonly byte _slotType;
    private readonly byte _slot;
    private readonly uint _templateId;
    private readonly int _itemCount;
    private readonly DateTime _removeReservationTime;

    public ItemRemove(Item item)
    {
        _type = ItemAction.Remove; // 7

        _itemId = item.Id;
        _slotType = (byte)item.SlotType;
        _slot = (byte)item.Slot;
        _templateId = item.TemplateId;
        _removeReservationTime = DateTime.MinValue;
        _itemCount = item.Count;
    }

    public ItemRemove(ulong itemId, SlotType slotType, byte slotNumber, uint itemTemplateId)
    {
        _type = ItemAction.Remove; // 7

        _itemId = itemId;
        _slotType = (byte)slotType;
        _slot = slotNumber;
        _templateId = itemTemplateId;
        _removeReservationTime = DateTime.MinValue;
        _itemCount = 1;

    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write(_slotType);              // type
        stream.Write(_slot);                  // index
        stream.Write(_itemId);                // id
        stream.Write(_itemCount);             // stack
        stream.Write(_removeReservationTime); // removeReservationTime
        stream.Write(_templateId);            // type
        stream.Write(0u);                     // dbSlaveId
        stream.Write(0u);                     // type

        return stream;
    }
}
