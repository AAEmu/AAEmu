using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCScheduleItemUpdatePacket : GamePacket
{
    private readonly List<ScheduleItem> _scheduleItems;

    public SCScheduleItemUpdatePacket(List<ScheduleItem> scheduleItems) : base(SCOffsets.SCScheduleItemUpdatePacket, 5)
    {
        _scheduleItems = scheduleItems;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_scheduleItems.Count);
        foreach (var item in _scheduleItems)
        {
            stream.Write(item);
        }
        return stream;
    }
}
