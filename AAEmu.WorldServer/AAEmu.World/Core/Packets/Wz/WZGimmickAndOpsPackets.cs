using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// GimmickSpawnData followed by ownerZoneId.
/// </summary>
public class WZGimmickCreatedPacket(GimmickSpawnData data, int ownerZoneId)
    : ZonePacket(WzOpcodes.GimmickCreated)
{
    protected override void WriteBody(PacketStream stream)
    {
        data.Write(stream);
        stream.Write(ownerZoneId);
    }
}

public class WZGimmickRemovedPacket(uint id) : ZonePacket(WzOpcodes.GimmickRemoved)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(id);
}

public class WZWorldGameTimePacket(uint time) : ZonePacket(WzOpcodes.WorldGameTime)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(time);
}

public class WZDetailedTimeOfDayPacket(float time, float speed, float start, float end)
    : ZonePacket(WzOpcodes.DetailedTimeOfDay)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(time);
        stream.Write(speed);
        stream.Write(start);
        stream.Write(end);
    }
}

public class WZSiegeStatePacket(byte[] body) : ZonePacket(WzOpcodes.SiegeState)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(body);
}

public class WZCheckMoleMinerPacket(byte[] body) : ZonePacket(WzOpcodes.CheckMoleMiner)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(body);
}

public class WZCheckMoleTraderPacket(byte[] body) : ZonePacket(WzOpcodes.CheckMoleTrader)
{
    protected override void WriteBody(PacketStream stream) => stream.Write(body);
}
