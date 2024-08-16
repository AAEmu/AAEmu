using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemRemove : ItemTask
{
    private readonly ulong _itemId;
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly uint _templateId;

    public ItemRemove(Item item)
    {
        _type = ItemAction.Remove;

        _itemId = item.Id;
        _slotType = item.SlotType;
        _slot = (byte)item.Slot;
        _templateId = item.TemplateId;
    }

    public ItemRemove(ulong itemId, SlotType slotType, byte slotNumber, uint itemTemplateId)
    {
        _type = ItemAction.Remove;

        _itemId = itemId;
        _slotType = slotType;
        _slot = slotNumber;
        _templateId = itemTemplateId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_slotType);
        stream.Write(_slot);
        stream.Write(_itemId);
        stream.Write(_templateId);
        return stream;
    }
}
