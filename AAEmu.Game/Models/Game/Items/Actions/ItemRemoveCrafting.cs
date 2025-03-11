using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemRemoveCrafting : ItemTask
{
    private readonly ulong _id;

    public ItemRemoveCrafting(ulong id)
    {
        _type = ItemAction.RemoveCrafting; // 12
        _id = id;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_id); // id
        return stream;
    }
}
