using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ChangeGamePoint : ItemTask
{
    private readonly byte _kind;
    private readonly int _amount;

    public ChangeGamePoint(byte kind, int amount)
    {
        _type = ItemAction.ChangeGamePoint; // 3
        _amount = amount;
        _kind = kind;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_kind);   // kind
        stream.Write(_amount); // amount
        return stream;
    }
}
