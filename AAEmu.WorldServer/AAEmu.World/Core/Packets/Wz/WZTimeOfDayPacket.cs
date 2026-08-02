using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>WZTimeOfDay (0x054) — game hour in [0..24).</summary>
public class WZTimeOfDayPacket(float hour) : ZonePacket(WzOpcodes.TimeOfDay)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(hour);
    }
}
