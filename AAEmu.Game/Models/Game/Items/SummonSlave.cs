using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SummonSlave : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Slave;
    public override uint DetailBytesLength => 29;

    public byte SlaveType { get; set; } // Not sure about this, captures show 2 here
    public uint SlaveDbId { get; set; }

    public SummonSlave()
    {
    }

    public SummonSlave(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        SlaveType = stream.ReadByte(); // Type? (2 = slave?)
        SlaveDbId = stream.ReadUInt32(); // DbId
        _ = stream.ReadBytes(24); // Filler, Equipment?
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(SlaveType);
        stream.Write(SlaveDbId);
        stream.Write(new byte[24]);
    }
}
