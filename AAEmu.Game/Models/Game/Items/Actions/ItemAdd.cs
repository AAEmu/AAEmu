using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemAdd : ItemTask
{
    private readonly Item _item;

    public ItemAdd(Item item)
    {
        _type = ItemAction.Create; // 5
        _item = item;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);

        stream.Write(_item.TemplateId);
        stream.Write(_item.Id);
        stream.Write(_item.Grade);
        stream.Write(_item.Flags);
        stream.Write(_item.Count);
        stream.Write((byte)_item.DetailType);

        _item.WriteDetails(stream);

        stream.Write(_item.CreateTime);
        stream.Write(_item.LifespanMins);
        stream.Write(_item.MadeUnitId);
        stream.Write(_item.WorldId);
        stream.Write(_item.UnsecureTime);
        stream.Write(_item.UnpackTime);
        stream.Write(_item.ChargeUseSkillTime);

        return stream;
    }
}
