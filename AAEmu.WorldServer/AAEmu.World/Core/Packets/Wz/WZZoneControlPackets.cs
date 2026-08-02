using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

// World → Zone zone control, physics and diagnostics (opcodes 0x070-0x0FF).
// Each body is the dedicate DLL's own serializer for the type, reached through slot 2

public class WZRequestCombatUnitsPacket(uint unitId, uint unitId2)
    : ZonePacket(WzOpcodes.RequestCombatUnits)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
    }
}

public class WZNpcBannedPacket(int typeValue, bool valueValue)
    : ZonePacket(WzOpcodes.NpcBanned)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(valueValue);
    }
}

public class WZVegetationCutdownPacket(uint unitId)
    : ZonePacket(WzOpcodes.VegetationCutdown)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
    }
}

public class WZRequestZoneStatusPacket()
    : ZonePacket(WzOpcodes.RequestZoneStatus)
{
    protected override void WriteBody(PacketStream stream) { }
}

public class WZPhysicsCPUPacket(uint count)
    : ZonePacket(WzOpcodes.PhysicsCPU)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(count);
    }
}

/// <summary>
/// vec3f direction, u32 id, bool isWaterLevelCasting, bool isTextInfo.
/// </summary>
public class WZRayCastingPacket(ulong playerId, ulong x, ulong y, float z, float dirx, float diry, float dirz, uint id, bool isWaterLevelCasting, bool isTextInfo)
    : ZonePacket(WzOpcodes.RayCasting)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(playerId);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(dirx);
        stream.Write(diry);
        stream.Write(dirz);
        stream.Write(id);
        stream.Write(isWaterLevelCasting);
        stream.Write(isTextInfo);
    }
}

public class WZAiDebugPacket(uint unitId, uint unitId2, ulong playerKey)
    : ZonePacket(WzOpcodes.AiDebug)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(playerKey);
    }
}

public class WZBuffLearnedPacket(uint unitId, int typeValue)
    : ZonePacket(WzOpcodes.BuffLearned)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
    }
}

public class WZSkillsResetPacket(uint unitId, byte ability)
    : ZonePacket(WzOpcodes.SkillsReset)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(ability);
    }
}

public class WZLevelChangedPacket(uint unitId, byte level)
    : ZonePacket(WzOpcodes.LevelChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(level);
    }
}

public class WZZombieCharacterPacket(uint unitId)
    : ZonePacket(WzOpcodes.ZombieCharacter)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
    }
}

public class WZConflictZoneStatePacket(short typeValue, byte hpws)
    : ZonePacket(WzOpcodes.ConflictZoneState)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(typeValue);
        stream.Write(hpws);
    }
}

public class WZSkillCooldownReducePacket(uint unitId, int typeValue, int typeValue2, uint percent, uint count, uint reduce, bool rstc, bool rtsc, bool rtstc)
    : ZonePacket(WzOpcodes.SkillCooldownReduce)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(percent);
        stream.Write(count);
        stream.Write(reduce);
        stream.Write(rstc);
        stream.Write(rtsc);
        stream.Write(rtstc);
    }
}

public class WZAddSwapPassiveBuffsPacket(uint unitId, int typeValue)
    : ZonePacket(WzOpcodes.AddSwapPassiveBuffs)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
    }
}

public class WZChangeChargeSkillCooldown(uint unitId, int typeValue, uint percent, uint count, uint reduce)
    : ZonePacket(WzOpcodes.ChangeChargeSkillCooldown)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
        stream.Write(percent);
        stream.Write(count);
        stream.Write(reduce);
    }
}

public class WZAttackFactionPacket(uint unitId, byte attckFactionFlags)
    : ZonePacket(WzOpcodes.AttackFaction)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(attckFactionFlags);
    }
}
