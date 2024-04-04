using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public abstract class ItemTask : PacketMarshaler
{
    protected ItemAction _type;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);  // tasks
        stream.Write((byte)0); // tLogt
        return stream;
    }
}
