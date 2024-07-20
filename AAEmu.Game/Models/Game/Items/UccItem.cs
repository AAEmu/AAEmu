using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class UccItem : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Ucc;
    public override uint DetailBytesLength => 9;

    public UccItem()
    {
    }

    public UccItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        UccId = stream.ReadUInt64();
        _ = stream.ReadByte();
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(UccId);
        stream.Write((byte)0);
    }
}
