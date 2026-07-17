using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCScheduleItemUpdatePacket(List<ScheduleItem> scheduleItems)
    : GamePacket(SCOffsets.SCScheduleItemUpdatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(scheduleItems.Count);
        foreach (var item in scheduleItems)
        {
            stream.Write(item);
        }
        return stream;
    }
}
