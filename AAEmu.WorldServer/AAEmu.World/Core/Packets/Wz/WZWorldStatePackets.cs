using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

// World → Zone world state, gimmicks, sieges and schedules (opcodes 0x050-0x06F).
// Each body is the dedicate DLL's own serializer for the type, reached through slot 2

public class WZAttackOnQuestPacket(uint unitId, uint unitId2)
    : ZonePacket(WzOpcodes.AttackOnQuest)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
    }
}

public class WZFollowUnitOnQuestPacket(uint unitId, uint unitId2)
    : ZonePacket(WzOpcodes.FollowUnitOnQuest)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
    }
}

public class WZFollowPathOnQuestPacket(uint unitId, uint unitId2, string pathName, byte pathType)
    : ZonePacket(WzOpcodes.FollowPathOnQuest)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(pathName ?? string.Empty);
        stream.Write(pathType);
    }
}

public class WZRunCommandSetOnQuestPacket(uint unitId, uint unitId2, int typeValue)
    : ZonePacket(WzOpcodes.RunCommandSetOnQuest)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(typeValue);
    }
}

public class WZSandboxOnlineHeightmapAction(string account, long actionNo, byte typeValue, uint posX, uint posY, float radius, float radiusInside, float height, float maxHeight, float hardness, bool noise, float noiseScale, float noiseFreq, bool repositionObjects)
    : ZonePacket(WzOpcodes.SandboxOnlineHeightmapAction)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(account ?? string.Empty);
        stream.Write(actionNo);
        stream.Write(typeValue);
        stream.Write(posX);
        stream.Write(posY);
        stream.Write(radius);
        stream.Write(radiusInside);
        stream.Write(height);
        stream.Write(maxHeight);
        stream.Write(hardness);
        stream.Write(noise);
        stream.Write(noiseScale);
        stream.Write(noiseFreq);
        stream.Write(repositionObjects);
    }
}

public class WZSandboxOnlineUndo(string account, long actionNo)
    : ZonePacket(WzOpcodes.SandboxOnlineUndo)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(account ?? string.Empty);
        stream.Write(actionNo);
    }
}

public class WZSandboxOnlinePlayerPos(string account, float posx, float posy, float posz, float rotx, float roty, float rotz, float rotw, float brushPosx, float brushPosy, float brushPosz, float brushRadius)
    : ZonePacket(WzOpcodes.SandboxOnlinePlayerPos)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(account ?? string.Empty);
        stream.Write(posx);
        stream.Write(posy);
        stream.Write(posz);
        stream.Write(rotx);
        stream.Write(roty);
        stream.Write(rotz);
        stream.Write(rotw);
        stream.Write(brushPosx);
        stream.Write(brushPosy);
        stream.Write(brushPosz);
        stream.Write(brushRadius);
    }
}

public class WZGimmickReloadStaticsPacket()
    : ZonePacket(WzOpcodes.GimmickReloadStatics)
{
    protected override void WriteBody(PacketStream stream) { }
}

public class WZGimmickMovementPacket(int id, int time, ulong x, ulong y, float z, float rotx, float roty, float rotz, float rotw, float velx, float vely, float velz, float angVelx, float angVely, float angVelz, float scale)
    : ZonePacket(WzOpcodes.GimmickMovement)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(time);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(rotx);
        stream.Write(roty);
        stream.Write(rotz);
        stream.Write(rotw);
        stream.Write(velx);
        stream.Write(vely);
        stream.Write(velz);
        stream.Write(angVelx);
        stream.Write(angVely);
        stream.Write(angVelz);
        stream.Write(scale);
    }
}

public class WZGimmickGraspedPacket(int id, int grasperUnitId, bool grasped)
    : ZonePacket(WzOpcodes.GimmickGrasped)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(grasperUnitId);
        stream.Write(grasped);
    }
}

public class WZDominionDeletedPacket()
    : ZonePacket(WzOpcodes.DominionDeleted)
{
    protected override void WriteBody(PacketStream stream) { }
}

public class WZSiegeMemberPacket(int typeValue, ulong typeValue2, bool added)
    : ZonePacket(WzOpcodes.SiegeMember)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(added);
    }
}

public class WZSiegeSecondHalfPacket(bool secondHalf)
    : ZonePacket(WzOpcodes.SiegeSecondHalf)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(secondHalf);
    }
}

public class WZTowerDefReloadPacket()
    : ZonePacket(WzOpcodes.TowerDefReload)
{
    protected override void WriteBody(PacketStream stream) { }
}

public class WZTowerDefQueryPlayabilityPacket(int typeValue, short typeValue2)
    : ZonePacket(WzOpcodes.TowerDefQueryPlayability)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
    }
}

public class WZTowerDefStartPacket(int typeValue, short typeValue2, uint spotIdx)
    : ZonePacket(WzOpcodes.TowerDefStart)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(spotIdx);
    }
}

public class WZTowerDefEndPacket(int typeValue, short typeValue2, uint spotIdx)
    : ZonePacket(WzOpcodes.TowerDefEnd)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(spotIdx);
    }
}

public class WZTowerDefWaveStartPacket(int typeValue, short typeValue2, uint spotIdx, uint step)
    : ZonePacket(WzOpcodes.TowerDefWaveStart)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(spotIdx);
        stream.Write(step);
    }
}

// VERIFIED 10.0.2.13: the three GameSchedule bodies each make exactly one serializer call, on
// same table gives 0xF0=f32 (WZTimeOfDay) and 0x30+0x38=Bc (WZUnitRemoved). The single field is
// the game_schedules row id. Layout confirmed field-by-field; no need to re-derive.
//
// These three are what release schedule-linked spawners: the dedicate withholds every placement
// named in game_schedule_spawners until World declares the period open, and never reads
// game_schedules itself. Sent by GameScheduleRelay.

/// <summary>GameScheduleStart (0x06A) — World → Zone. Body: u32 gameScheduleId.</summary>
public class WZGameScheduleStartPacket(int typeValue)
    : ZonePacket(WzOpcodes.GameScheduleStart)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
    }
}

/// <summary>GameScheduleContinue (0x06B) — World → Zone. Body: u32 gameScheduleId.</summary>
public class WZGameScheduleContinuePacket(int typeValue)
    : ZonePacket(WzOpcodes.GameScheduleContinue)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
    }
}

/// <summary>GameScheduleEnd (0x06C) — World → Zone. Body: u32 gameScheduleId.</summary>
public class WZGameScheduleEndPacket(int typeValue)
    : ZonePacket(WzOpcodes.GameScheduleEnd)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
    }
}

public class WZGameActivityStartPacket(uint activityId, uint serverId)
    : ZonePacket(WzOpcodes.GameActivityStart)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(activityId);
        stream.Write(serverId);
    }
}

public class WZGameActivityEndPacket(uint activityId, uint serverId)
    : ZonePacket(WzOpcodes.GameActivityEnd)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(activityId);
        stream.Write(serverId);
    }
}

public class WZNpcControlPacket(uint unitId, uint unitId2, int typeValue)
    : ZonePacket(WzOpcodes.NpcControl)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(typeValue);
    }
}
