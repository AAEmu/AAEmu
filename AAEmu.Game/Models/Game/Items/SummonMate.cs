using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SummonMate : Item
{
    public int DetailMateExp { get; set; }
    public byte DetailLevel { get; set; }

    public override ItemDetailType DetailType => ItemDetailType.Mate;
    public override uint DetailBytesLength => 6;

    public SummonMate()
    {
    }

    public SummonMate(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        DetailMateExp = stream.ReadInt32(); // exp
        _ = stream.ReadByte();
        DetailLevel = stream.ReadByte(); // level
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(DetailMateExp); // exp
        stream.Write((byte)0);
        stream.Write(DetailLevel); // level
    }
}
