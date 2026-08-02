using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ChangeBankAAPoint : ItemTask
{
    private readonly long _amount;

    public ChangeBankAAPoint(long amount)
    {
        _type = ItemAction.ChangeBankAaPoint;
        _amount = amount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_amount);
        return stream;
    }
}
