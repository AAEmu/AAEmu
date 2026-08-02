using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class BigFish : Item
{
    private float _weight;
    private float _length;
    private long _detailQword;

    public override ItemDetailType DetailType => ItemDetailType.BigFish;
    public override uint DetailBytesLength => 16;

    public float Weight { get => _weight; set { _weight = value; IsDirty = true; } }
    public float Length { get => _length; set { _length = value; IsDirty = true; } }
    public long DetailQword { get => _detailQword; set { _detailQword = value; IsDirty = true; } }

    public BigFish()
    {
    }

    public BigFish(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        /*
         Length = 4
         Weight = 4
         Opaque qword = 8
         */
        Weight = stream.ReadSingle();
        Length = stream.ReadSingle();
        DetailQword = stream.ReadInt64();
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(Weight);     // Weight
        stream.Write(Length);     // Length
        stream.Write(DetailQword);
    }
}
