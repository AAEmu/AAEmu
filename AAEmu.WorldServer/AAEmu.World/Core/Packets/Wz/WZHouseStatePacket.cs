using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>WZHouseState (0x006) — materialize house on Zone (requires prior WZUnitState for house unit).</summary>
public class WZHouseStatePacket(byte[] body) : ZonePacket(WzOpcodes.HouseState)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(body);
}

public class WZHouseBuildDonePacket(ushort tl) : ZonePacket(WzOpcodes.HouseBuildDone)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(tl);
}

public class WZHouseBuildProgressPacket(ushort tl, uint type, int allStep, int curStep)
    : ZonePacket(WzOpcodes.HouseBuildProgress)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(type);
        stream.Write(allStep);
        stream.Write(curStep);
    }
}
