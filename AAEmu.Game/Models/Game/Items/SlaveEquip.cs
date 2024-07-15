using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SlaveEquip : Item
{
    public override ItemDetailType DetailType => ItemDetailType.SlaveEquipment;
    public override uint DetailBytesLength => 12;

    public uint SlaveHp { get; set; } // Not sure about this

    public SlaveEquip()
    {
    }

    public SlaveEquip(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        SlaveHp = stream.ReadUInt32(); // HP
        stream.ReadUInt32();           // mb DataTime?
        stream.ReadUInt32();
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(SlaveHp);
        stream.Write(0);
        stream.Write(0);
    }
}
