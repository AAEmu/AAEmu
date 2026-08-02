using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZGimmickCreated (0x05A) — GimmickSpawnData + ownerZoneId (WZ_BRINGONLINE §1).
/// </summary>
public class WZGimmickCreatedPacket(
    uint id,
    uint type,
    uint ownerZoneId,
    string modelPath,
    float x,
    float y,
    float z,
    float scale) : ZonePacket(WzOpcodes.GimmickCreated)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(type);
        stream.Write(0ul); // entityGUID
        stream.Write(0u); // type2
        stream.Write(0u); // spawnerUnitId
        stream.Write(0u); // grasperUnitId
        stream.Write((int)ownerZoneId); // staticZoneId
        stream.Write(modelPath ?? "");
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        // rot quat identity
        stream.Write(0f);
        stream.Write(0f);
        stream.Write(0f);
        stream.Write(1f);
        stream.Write(scale);
        stream.Write(0f); stream.Write(0f); stream.Write(0f); // vel
        stream.Write(0f); stream.Write(0f); stream.Write(0f); // angVel
        stream.Write(0f); // scaleVel
        stream.Write((int)ownerZoneId);
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
