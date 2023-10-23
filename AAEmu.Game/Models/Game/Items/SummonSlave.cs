using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SummonSlave : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Slave;
    public override uint DetailBytesLength => 29;

    public byte SlaveType { get; set; } // Not sure about this, captures show 2 here
    public uint SlaveDbId { get; set; }

    public DateTime DeathTime { get; set; }

    // TODO: Actually use this location for saving the data in ItemDetails
    public Vector3 SummonLocation { get; set; }

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
        try
        {
            // Read time of something else than 0
            var timeBytes = stream.ReadBytes(4);
            if (Convert.ToInt32(timeBytes) != 0)
                DeathTime = Convert.ToDateTime(timeBytes);
            else
                DeathTime = DateTime.MinValue;

            // Read remaining bytes
            _ = stream.ReadBytes((int)DetailBytesLength-1-4-4); // Filler, Equipment?
        }
        catch
        {
            DeathTime = DateTime.MinValue;
        }
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(SlaveType);
        stream.Write(SlaveDbId);

        if (DeathTime == DateTime.MinValue)
            stream.Write(0);
        else
            stream.Write(DeathTime);

        stream.Write(0); // If this is anything besides 0, it will count as being in recovering (negative at that)

        // The following 16 bytes somehow determine where a Vehicle is allowed to be summoned
        // TODO: Get real live data capture of this value being set
        // TODO: Get this from having a vehicle out when maintenance starts
        stream.Write(0);
        stream.Write(0);
        stream.Write(0);
        stream.Write(0);
    }
}
