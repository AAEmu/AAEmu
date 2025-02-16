using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ChangeBankAAPoint : ItemTask
{
    private readonly int _amount;

    public ChangeBankAAPoint(int amount)
    {
        _type = ItemAction.ChangeBankAaPoint; // 17
        _amount = amount;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_amount); // amount
        return stream;
    }
}
