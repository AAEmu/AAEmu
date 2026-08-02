using AAEmu.Commons.Network;

using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.World.Core.Packets.Wz;

// World → Zone combat, skills, buffs and aggro (opcodes 0x020-0x04F).
// Each body is the dedicate DLL's own serializer for the type, reached through slot 2

public class WZUnitResurrectionPacket(uint unitId, ulong x, ulong y, float z, float zRot)
    : ZonePacket(WzOpcodes.UnitResurrection)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(zRot);
    }
}

public class WZOnUnitBlinkedPacket(uint unitId, float distance)
    : ZonePacket(WzOpcodes.OnUnitBlinked)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(distance);
    }
}

public class WZForceAttackSetPacket(uint unitId, bool on)
    : ZonePacket(WzOpcodes.ForceAttackSet)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(on);
    }
}

public class WZCombatEngagedPacket(uint unitId)
    : ZonePacket(WzOpcodes.CombatEngaged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
    }
}

public class WZCombatClearedPacket(uint unitId)
    : ZonePacket(WzOpcodes.CombatCleared)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
    }
}

public class WZUnitDuelStatePacket(uint unitId, uint unitId2, byte duelTeamType)
    : ZonePacket(WzOpcodes.UnitDuelState)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(duelTeamType);
    }
}

public class WZTargetChangedPacket(uint unitId, uint unitId2, bool forceByWorld)
    : ZonePacket(WzOpcodes.TargetChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(forceByWorld);
    }
}

public class WZSkillStoppedPacket(uint unitId, int typeValue)
    : ZonePacket(WzOpcodes.SkillStopped)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
    }
}

public class WZCastingStoppedPacket(uint unitId, short tl, int typeValue, int duration)
    : ZonePacket(WzOpcodes.CastingStopped)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(tl);
        stream.Write(typeValue);
        stream.Write(duration);
    }
}

public class WZBlinkUnitPacket(uint unitId, uint unitId2, bool move3D, ulong x, ulong y, float z)
    : ZonePacket(WzOpcodes.BlinkUnit)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(unitId2);
        stream.Write(move3D);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
    }
}

public class WZKnockBackUnitPacket(uint unitId, float posx, float posy, float posz)
    : ZonePacket(WzOpcodes.KnockBackUnit)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(posx);
        stream.Write(posy);
        stream.Write(posz);
    }
}

public class WZUnitInvisiblePacket(uint unitId, bool invisible)
    : ZonePacket(WzOpcodes.UnitInvisible)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(invisible);
    }
}

public class WZRequestUnitFlyingStatePacket(uint unitId, bool isFlying)
    : ZonePacket(WzOpcodes.RequestUnitFlyingState)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(isFlying);
    }
}

public class WZSkillCooldownResetPacket(uint unitId, int typeValue, int typeValue2, bool gc, bool rstc, bool rtsc, bool rtstc)
    : ZonePacket(WzOpcodes.SkillCooldownReset)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
        stream.Write(typeValue2);
        stream.Write(gc);
        stream.Write(rstc);
        stream.Write(rtsc);
        stream.Write(rtstc);
    }
}

public class WZPlotEndedPacket(short tl)
    : ZonePacket(WzOpcodes.PlotEnded)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(tl);
    }
}

public class WZPlotCastingStoppedPacket(short tl, int duration, bool lastEvent)
    : ZonePacket(WzOpcodes.PlotCastingStopped)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(duration);
        stream.Write(lastEvent);
    }
}

public class WZPlotChannelingStoppedPacket(short tl, int duration, bool lastEvent)
    : ZonePacket(WzOpcodes.PlotChannelingStopped)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(duration);
        stream.Write(lastEvent);
    }
}

public class WZBuffUpdatedPacket(uint unitId, int typeValue, uint stack, uint charged, int elapsedTime, byte reason)
    : ZonePacket(WzOpcodes.BuffUpdated)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(typeValue);
        stream.Write(stack);
        stream.Write(charged);
        stream.Write(elapsedTime);
        stream.Write(reason);
    }
}

// Nothing sent this before, and that is why scripted-AI NPCs never fought back: the dedicate's
// CryAI keeps its own aggro table, and with World never publishing one its log reports
// "Removing all aggros. Npc: …, Behavior: idle, AggroCount: 0". Simple mobs still engaged because
// they acquire aggro themselves through sight (alertDuration 3.0), but an almighty_npc boss like
// the Kraken runs alertDuration = 0 — behaviors/default.lua then takes the "I don't want to use
// Alert state" branch and only aggros if told, so it sat idle no matter how long it was attacked.
/// <summary>
/// UpdateAggro (0x044) — World → Zone: publish an aggro entry so the dedicate's AI can enter
/// <c>source</c>, and <c>unitInCharge</c>, followed by aggro(u32), hostile(bool), and the complete
/// friendly/hostile aggro path from the bool.
/// </summary>
public class WZUpdateAggroPacket(
    uint skillTargetId,
    uint sourceId,
    uint unitInChargeId,
    uint aggro,
    bool hostile,
    CastAction castAction)
    : ZonePacket(WzOpcodes.UpdateAggro)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(skillTargetId);
        stream.WriteBc(sourceId);
        stream.WriteBc(unitInChargeId);
        stream.Write(aggro);
        stream.Write(hostile);
        stream.Write(castAction);
    }
}

/// <summary>
/// <c>bc unit, i32 damageSelector, i32 healSelector, i32 directSelector, i32 applyValue</c>.
/// replaces its component on all entries. Four zero selector/value fields select the native
/// full-clear path.
/// </summary>
public class WZAggroResetPacket(
    uint unitId,
    int damageSelector,
    int healSelector,
    int directSelector,
    int applyValue)
    : ZonePacket(WzOpcodes.AggroReset)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(damageSelector);
        stream.Write(healSelector);
        stream.Write(directSelector);
        stream.Write(applyValue);
    }
}

/// <summary>
/// does not mutate HP or real death state.
/// </summary>
public class WZFakeDeathPacket(uint unitId)
    : ZonePacket(WzOpcodes.FakeDeath)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
    }
}

/// <summary>
/// </summary>
public class WZAggroCopyPacket(uint sourceUnitId, uint destinationUnitId)
    : ZonePacket(WzOpcodes.AggroCopy)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(sourceUnitId);
        stream.WriteBc(destinationUnitId);
    }
}

public class WZShipControlChangePacket(uint unitId, bool control)
    : ZonePacket(WzOpcodes.ShipControlChange)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(control);
    }
}

public class WZEscapeSlavePacket(uint unitId, ulong x, ulong y, float z, float rot)
    : ZonePacket(WzOpcodes.EscapeSlave)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(rot);
    }
}
