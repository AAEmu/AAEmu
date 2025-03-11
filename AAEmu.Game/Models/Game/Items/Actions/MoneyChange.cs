using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class MoneyChange : ItemTask
{
    private readonly int _amount;

    public MoneyChange(int amount)
    {
        _type = ItemAction.ChangeMoneyAmount; // 1
        _amount = amount;
        _tLogt = SetTlogT(_type, SlotType.Bag, _amount < 0); // установим tLogt по значению ItemAction

    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_amount); // amount
        return stream;
    }
}
