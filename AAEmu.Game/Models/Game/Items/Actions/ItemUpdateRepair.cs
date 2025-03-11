using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemUpdateRepair : ItemTask
{
    private readonly Item _item;

    public ItemUpdateRepair(Item item)
    {
        _type = ItemAction.UpdateDetail; // 9
        _item = item;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);
        stream.Write(_item.Id);

        var details = new PacketStream();
        details.Write((byte)_item.DetailType);

        details.Write(0u); // добавил для нормальной работы починки предметов

        _item.WriteDetails(details);

        stream.Write((short)128);
        stream.Write(details, false);
        stream.Write(new byte[128 - details.Count]);

        return stream;
    }
}
