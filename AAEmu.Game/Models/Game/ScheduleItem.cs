using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game;

public class ScheduleItem : PacketMarshaler
{
    public uint ScheduleItemId { get; set; }
    public byte Gave { get; set; }
    public uint Cumulated { get; set; }
    public DateTime Updated { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ScheduleItemId);
        stream.Write(Gave);
        stream.Write(Cumulated);
        stream.Write(Updated);
        return stream;
    }
}
