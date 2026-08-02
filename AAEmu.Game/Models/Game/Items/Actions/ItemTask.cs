using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public abstract class ItemTask : PacketMarshaler
{
    protected ItemAction _type;
    protected byte _tLogt;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);
        stream.Write(_tLogt);
        return stream;
    }

    public virtual void ReadDetails(PacketStream stream)
    {
    }

    // Item payload for full-item tasks. v10 serializes via the canonical Item writer
    // the chargeUseSkillTime trailer. The slot (type/index) is written by the owning task before this call.
    protected virtual void WriteDetails(PacketStream stream, Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Write(stream);
    }
}
