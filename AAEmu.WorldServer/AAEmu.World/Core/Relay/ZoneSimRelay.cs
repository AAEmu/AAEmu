using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Zw;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Zone→World sim events beyond NPC spawn/move/combat: areas, housing, gimmicks,
/// doodad collide, backpack, mole/hack, tower-def, combat-unit queries.
/// </summary>
public class ZoneSimRelay
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public bool TryHandle(ushort opcode, byte[] body, int bodyLen)
        => TryHandle(0, opcode, body, bodyLen);

    public bool TryHandle(ZoneConnection connection, ushort opcode, byte[] body, int bodyLen)
        => TryHandle(connection.ZoneId, opcode, body, bodyLen);

    private static bool TryHandle(uint zoneId, ushort opcode, byte[] body, int bodyLen)
    {
        var stream = new PacketStream(body);
        return opcode switch
        {
            ZwOpcodes.CommandResponse => HandleCommandResponse(stream),
            ZwOpcodes.EnterArea => HandleEnterArea(stream),
            ZwOpcodes.LeaveArea => HandleLeaveArea(stream),
            ZwOpcodes.NpcSaid => HandleNpcSaid(stream),
            ZwOpcodes.UnitModelPostureChanged => RelayIdenticalUnitPacket(
                "ZWUnitModelPostureChanged", SCOffsets.SCUnitModelPostureChangedPacket, body, stream),
            ZwOpcodes.UnitFell => HandleUnitFell(stream),
            ZwOpcodes.UnitCollision => HandleUnitCollision(stream),
            ZwOpcodes.UnitCollisionResult => HandleUnitCollisionResult(stream),
            ZwOpcodes.ErrorMsgToPlayer => HandleErrorMsgToPlayer(stream),
            ZwOpcodes.NpcInteractionEnded => HandleNpcInteractionEnded(stream),
            ZwOpcodes.ChangeTarget => HandleChangeTarget(stream),
            ZwOpcodes.ChangeUnitFlyState => RelayIdenticalUnitPacket(
                "ZWChangeUnitFlyState", SCOffsets.SCUnitFlyingStateChangedPacket, body, stream),
            ZwOpcodes.NpcInteractionStatusChanged => RelayIdenticalUnitPacket(
                "ZWNpcInteractionStatusChanged", SCOffsets.SCNpcInteractionStatusChangedPacket, body, stream),
            ZwOpcodes.AggroTargetChanged => HandleAggroTargetChanged(stream),
            ZwOpcodes.RayCastingResult => HandleRayCastingResult(stream),
            ZwOpcodes.RemoveHouse => HandleRemoveHouse(stream),
            ZwOpcodes.GimmickMovement => RelayGimmickMovement(body, bodyLen),
            ZwOpcodes.RequestSpawnStaticGimmick => HandleRequestSpawnStaticGimmick(zoneId, stream, bodyLen),
            ZwOpcodes.GimmickJointBroken => HandleGimmickJointBroken(stream),
            ZwOpcodes.GimmickResetJoints => HandleGimmickResetJoints(stream),
            ZwOpcodes.GimmickRemoveParts => HandleGimmickRemoveParts(stream, bodyLen),
            ZwOpcodes.GimmickCollided => HandleGimmickCollided(stream),
            ZwOpcodes.DoodadCollideUnit => HandleDoodadCollide(stream),
            ZwOpcodes.BackpackDropped => HandleBackpackDropped(stream, bodyLen),
            ZwOpcodes.ReportMoleMiner => HandleMole("ZWReportMoleMiner", bodyLen),
            ZwOpcodes.ReportMoleTrader => HandleMole("ZWReportMoleTrader", bodyLen),
            ZwOpcodes.ReportMoveHack => HandleMole("ZWReportMoveHack", bodyLen),
            ZwOpcodes.ReportDoodadSurfaceHack => HandleMole("ZWReportDoodadSurfaceHack", bodyLen),
            ZwOpcodes.TowerDefReportPlayability => HandleTowerDef(zoneId, stream),
            ZwOpcodes.ResponseCombatUnits => HandleResponseCombatUnits(stream, bodyLen),
            ZwOpcodes.TimeOfDay => HandleTimeOfDay(zoneId, stream, detailed: false),
            ZwOpcodes.DetailedTimeOfDay => HandleTimeOfDay(zoneId, stream, detailed: true),
            _ => false
        };
    }

    /// <summary>
    /// ZWCommandResponse opcode 0x001: u32 request id and a string capped at 0x1ff bytes by
    /// </summary>
    private static bool HandleCommandResponse(PacketStream stream)
    {
        if (stream.Count < sizeof(uint) + sizeof(ushort))
            return false;

        try
        {
            var requestId = stream.ReadUInt32();
            var result = stream.ReadString() ?? string.Empty;
            if (stream.Count != 0)
                return false;

            Logger.Debug("ZWCommandResponse id={0} result={1}", requestId, result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// </summary>
    private static bool HandleRayCastingResult(PacketStream stream)
    {
        if (stream.Count < sizeof(ulong) + sizeof(uint) + sizeof(ulong) * 2 + sizeof(float) + sizeof(ushort))
            return false;

        try
        {
            var playerId = stream.ReadUInt64();
            var id = stream.ReadUInt32();
            var x = stream.ReadUInt64();
            var y = stream.ReadUInt64();
            var z = stream.ReadSingle();
            var text = stream.ReadString() ?? string.Empty;
            if (stream.Count != 0)
                return false;

            WorldIntegration.OnZoneRayCastingResult?.Invoke(playerId, id, x, y, z, text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HandleEnterArea(PacketStream stream)
    {
        if (stream.Count < 12)
            return false;
        var unitId = stream.ReadBc();
        var areaId = stream.ReadByte();
        var v1 = stream.ReadInt32();
        var v2 = stream.ReadInt32();
        Logger.Info("ZWEnterArea unit={0} area={1} v1={2} v2={3}", unitId, areaId, v1, v2);
        WorldIntegration.OnZoneEnterArea?.Invoke(unitId, areaId, v1, v2);
        return true;
    }

    private static bool HandleLeaveArea(PacketStream stream)
    {
        if (stream.Count < 12)
            return false;
        var unitId = stream.ReadBc();
        var areaId = stream.ReadByte();
        var v1 = stream.ReadInt32();
        var v2 = stream.ReadInt32();
        Logger.Info("ZWLeaveArea unit={0} area={1} v1={2} v2={3}", unitId, areaId, v1, v2);
        WorldIntegration.OnZoneLeaveArea?.Invoke(unitId, areaId, v1, v2);
        return true;
    }

    /// <summary>
    /// string text when kind=0, otherwise u32 type for kinds 1 and 2 }.
    /// </summary>
    private static bool HandleNpcSaid(PacketStream stream)
    {
        if (stream.Count < 7)
            return false;

        var speakerId = stream.ReadBc();
        var targetId = stream.ReadBc();
        var kind = stream.ReadByte();
        uint type = 0;
        var message = string.Empty;
        if (kind == 0)
            message = stream.ReadString() ?? string.Empty;
        else if (kind is 1 or 2)
            type = stream.ReadUInt32();

        // Mirrors live in whichever world owns their zone, not only MainWorld.
        if (WorldIntegration.FindUnitAcrossWorlds(speakerId) is not Npc npc)
            return true;

        var target = targetId == 0 ? null : WorldManager.Instance.GetCharacterByObjId(targetId);
        WorldIntegration.BroadcastPacketToUnitViewers(
            new SCNpcChatMessagePacket(ChatType.White, npc, target, kind, type, message),
            speakerId,
            targetId);
        Logger.Info("ZWNpcSaid speaker={0} target={1} kind={2}", speakerId, targetId, kind);
        return true;
    }

    /// <summary>
    /// </summary>
    private static bool HandleNpcInteractionEnded(PacketStream stream)
    {
        if (stream.Count < 6)
            return false;

        var playerId = stream.ReadBc();
        var npcId = stream.ReadBc();
        var player = WorldManager.Instance.GetCharacterByObjId(playerId);
        if (player != null)
        {
            var body = new PacketStream();
            body.WriteBc(npcId);
            player.SendPacket(new SCOpaquePacket(
                SCOffsets.SCNpcInteractionEndedByZonePacket, body.GetBytes()));
        }
        return true;
    }

    /// <summary>
    /// Zone sends the unchanged positive fall-impact speed magnitude;
    /// produced by its fall detector; World owns HP, stun and environmental-damage presentation.
    /// </summary>
    private static bool HandleUnitFell(PacketStream stream)
    {
        if (stream.Count < 7)
            return false;

        var unitId = stream.ReadBc();
        var impactVel = stream.ReadSingle();
        if (!float.IsFinite(impactVel) || impactVel <= 0f)
        {
            Logger.Warn("Rejected ZWUnitFell unit={0} impactVel={1}", unitId, impactVel);
            return true;
        }

        if (WorldIntegration.FindUnitAcrossWorlds(unitId) is not Unit unit || unit.Hp <= 0)
            return true;

        var damage = unit.DoFallDamage(impactVel);
        if (damage <= 0)
            return true;

        ZoneAuthorityCombat.SyncUnitPoints(unit.ObjId, unit.Hp, unit.Mp);
        if (unit.Hp <= 0 && unit is not Npc)
            WorldIntegration.RelayUnitDeathToZone?.Invoke(unit.ObjId);

        Logger.Info(
            "ZWUnitFell unit={0} impactVel={1:F2}m/s damage={2} hp={3}/{4}",
            unitId, impactVel, damage, unit.Hp, unit.MaxHp);
        return true;
    }

    /// <summary>
    /// bc trgUnit, u8 trgPart, f32 trgMass, f32 impact, long x, long y, f32 z</c>.
    /// Zone's collision manager aggregates contacts per unit pair, keeps the hardest impact and
    /// flushes them on a timer; either side can be terrain, in which case its unit id is a sentinel
    /// no World unit answers to. Knockback stays Zone-owned — World turns this into hull damage and
    /// the client's collision floater.
    /// </summary>
    private static bool HandleUnitCollision(PacketStream stream)
    {
        if (stream.Count < 3 + 1 + 4 + 3 + 1 + 4 + 4 + 8 + 8 + 4)
            return false;

        var srcUnit = stream.ReadBc();
        var srcPart = stream.ReadByte();
        var srcMass = stream.ReadSingle();
        var trgUnit = stream.ReadBc();
        var trgPart = stream.ReadByte();
        var trgMass = stream.ReadSingle();
        var impact = stream.ReadSingle();
        var x = stream.ReadInt64();
        var y = stream.ReadInt64();
        var z = stream.ReadSingle();

        Logger.Info(
            "ZWUnitCollision src={0} part={1} mass={2:F0} trg={3} part={4} mass={5:F0} impact={6:F2}",
            srcUnit, srcPart, srcMass, trgUnit, trgPart, trgMass, impact);

        _sawAggregatedCollisions = true;
        ApplyCollisionDamage(srcUnit, srcPart, impact, srcMass, x, y, z);
        ApplyCollisionDamage(trgUnit, trgPart, impact, trgMass, x, y, z);
        return true;
    }

    private static void ApplyCollisionDamage(
        uint bcId, byte part, float impact, float mass, long x, long y, float z)
    {
        if (WorldIntegration.FindUnitAcrossWorlds(bcId) is Slave slave)
            SlaveCollisionDamage.ApplyFromImpact(slave, (SlaveCollisionPart)part, impact, mass, x, y, z);
    }

    /// <summary>
    /// ZW 0x12: <c>bc unit + long x/y + f32 z + f32 impactVel</c> → SCUnitCollisionResult.
    /// Sent per contact for the client-side reaction; it carries no hull face, so damage is driven
    /// off the aggregated ZWUnitCollision instead and this stays a pure relay.
    /// </summary>
    private static bool HandleUnitCollisionResult(PacketStream stream)
    {
        if (stream.Count < 3 + 8 + 8 + 4 + 4)
            return false;
        var unitId = stream.ReadBc();
        var x = stream.ReadUInt64();
        var y = stream.ReadUInt64();
        var z = stream.ReadSingle();
        var impactVel = stream.ReadSingle();
        WorldIntegration.BroadcastPacketToUnitViewers(
            new SCUnitCollisionResultPacket(x, y, z, impactVel), unitId);
        Logger.Info("ZWUnitCollisionResult → SC unit={0} impactVel={1:F2}", unitId, impactVel);

        // Zone's aggregated flush is behind its own interval CVar. If a dedicate only ever sends the
        // per-contact result, hulls would take no damage at all, so fall back to it — throttled to
        // roughly the flush cadence, and only until a real ZWUnitCollision proves the flush is live.
        if (!_sawAggregatedCollisions)
            ApplyThrottledCollisionDamage(unitId, impactVel, (long)x, (long)y, z);
        return true;
    }

    /// <summary>True once a dedicate has sent an aggregated ZWUnitCollision, which owns damage.</summary>
    private static bool _sawAggregatedCollisions;

    private static readonly Dictionary<uint, DateTime> LastResultDamage = [];
    private static readonly TimeSpan ResultDamageInterval = TimeSpan.FromSeconds(1);

    private static void ApplyThrottledCollisionDamage(uint bcId, float impact, long x, long y, float z)
    {
        var now = DateTime.UtcNow;
        lock (LastResultDamage)
        {
            if (LastResultDamage.TryGetValue(bcId, out var last) && now - last < ResultDamageInterval)
                return;
            LastResultDamage[bcId] = now;
        }

        // No hull face on this packet; a hit hard enough to report is nearly always a side scrape.
        ApplyCollisionDamage(bcId, (byte)SlaveCollisionPart.Side, impact, 0f, x, y, z);
    }

    /// <summary>ZW 0x13: u64 type (player key) + u16 ErrorMessage → SCErrorMsg.</summary>
    private static bool HandleErrorMsgToPlayer(PacketStream stream)
    {
        if (stream.Count < 10)
            return false;
        var type = stream.ReadUInt64();
        var errorMessage = stream.ReadUInt16();
        // type is often the character objId or account key; try ObjId first.
        var player = type <= uint.MaxValue
            ? WorldManager.Instance.GetCharacterByObjId((uint)type)
            : null;
        if (player == null)
        {
            Logger.Info("ZWErrorMsgToPlayer type={0} msg={1} — no player", type, errorMessage);
            return true;
        }

        player.SendPacket(new SCErrorMsgPacket(
            (AAEmu.Game.Models.Game.ErrorMessageType)errorMessage, 0, true));
        Logger.Info("ZWErrorMsgToPlayer → SC player={0} msg={1}", player.ObjId, errorMessage);
        return true;
    }

    /// <summary>
    /// </summary>
    private static bool HandleChangeTarget(PacketStream stream)
    {
        if (stream.Count < 6)
            return false;

        var sourceId = stream.ReadBc();
        var targetId = stream.ReadBc();
        var player = WorldManager.Instance.GetCharacterByObjId(sourceId);
        if (player == null)
            return true;

        player.CurrentTarget = targetId == 0 ? null : player.ParentWorld.GetUnit(targetId);
        player.SendPacket(new SCTargetChangedPacket(sourceId, targetId));
        return true;
    }

    /// <summary>ZW 0x2E serializes { bc npc, bc target }, the same body SC 0x0B0 expects.</summary>
    private static bool HandleAggroTargetChanged(PacketStream stream)
    {
        if (stream.Count < 6)
            return false;

        var npcId = stream.ReadBc();
        var targetId = stream.ReadBc();

        if (WorldIntegration.FindUnitAcrossWorlds(npcId) is Npc npc)
        {
            // Assign the passive mirror property directly: Npc.SetTarget also
            // broadcasts SCTargetChanged and would duplicate the ZW relay below.
            npc.CurrentTarget = targetId == 0
                ? null
                : npc.ParentWorld?.GetUnit(targetId);
        }

        WorldIntegration.BroadcastPacketToUnitViewers(
            new SCTargetChangedPacket(npcId, targetId), npcId, targetId);
        return true;
    }

    private static bool RelayIdenticalUnitPacket(
        string name, ushort scOpcode, byte[] body, PacketStream stream)
    {
        if (stream.Count < 3)
            return false;

        var unitId = stream.ReadBc();
        WorldIntegration.BroadcastPacketToUnitViewers(new SCOpaquePacket(scOpcode, body), unitId);
        Logger.Debug("{0} unit={1} len={2}", name, unitId, body.Length);
        return true;
    }

    private static bool HandleRemoveHouse(PacketStream stream)
    {
        if (stream.Count != sizeof(ushort))
            return false;

        var tl = stream.ReadUInt16();
        Logger.Info("ZWRemoveHouse tl={0}", tl);
        WorldIntegration.OnZoneRemoveHouse?.Invoke(tl);
        return true;
    }

    /// <summary>
    /// Forwards an identical Zone gimmick body to clients as its SC counterpart.
    /// </summary>
    /// <remarks>
    /// This is used only when the native ZW and SC serializers have the same body layout.
    /// </remarks>
    private static bool RelayGimmickEvent(string name, ushort scOpcode, byte[] body, int bodyLen)
    {
        WorldIntegration.BroadcastSc(scOpcode, body);
        Logger.Debug("{0} len={1} relayed as SC 0x{2:X3}", name, bodyLen, scOpcode);
        return true;
    }

    /// <summary>
    /// an i8 collection capped at 200, so the Zone event must be converted rather than copied.
    /// </summary>
    private static bool HandleGimmickJointBroken(PacketStream stream)
    {
        if (stream.Count != sizeof(uint) + sizeof(int) + sizeof(int))
            return false;

        var joint = new GimmickJointBreak(
            stream.ReadUInt32(),
            stream.ReadInt32(),
            stream.ReadInt32());

        WorldIntegration.BroadcastPacket(new SCGimmickJointsBrokenPacket([joint]));
        Logger.Debug(
            "ZWGimmickJointBroken gimmick={0} joint={1} epicenter={2} relayed as SC 0x{3:X3}",
            joint.GimmickId,
            joint.JointId,
            joint.Epicenter,
            SCOffsets.SCGimmickJointsBrokenPacket);
        return true;
    }

    /// <summary>
    /// </summary>
    private static bool HandleGimmickResetJoints(PacketStream stream)
    {
        if (stream.Count != sizeof(uint))
            return false;

        var gimmickId = stream.ReadUInt32();
        WorldIntegration.BroadcastPacket(new SCGimmickResetJointsPacket(gimmickId));
        Logger.Debug(
            "ZWGimmickResetJoints gimmick={0} relayed as SC 0x{1:X3}",
            gimmickId,
            SCOffsets.SCGimmickResetJointsPacket);
        return true;
    }

    /// <summary>
    /// Relays a zone gimmick movement report, unless this world drives that gimmick itself.
    /// </summary>
    /// <remarks>
    /// The level's gimmicks are spawned here and announced to the zone, and Gimmick.GimmickTick
    /// already broadcasts their movement. Forwarding a zone report for one of those would put two
    /// updates on the wire for the same object each tick, so ownership is checked first. Gimmicks
    /// the zone spawned are unknown here and pass through.
    /// </remarks>
    private static bool RelayGimmickMovement(byte[] body, int bodyLen)
    {
        var head = new PacketStream(body);
        if (head.Count >= sizeof(uint))
        {
            var gimmickId = head.ReadUInt32();
            if (WorldIntegration.IsWorldOwnedGimmick?.Invoke(gimmickId) == true)
            {
                Logger.Debug("ZWGimmickMovement gimmick={0} ignored - driven by this world", gimmickId);
                return true;
            }
        }

        return RelayGimmickEvent("ZWGimmickMovement", SCOffsets.SCGimmickMovementPacket, body, bodyLen);
    }

    private static bool HandleGimmickLog(string name, PacketStream stream, int bodyLen)
    {
        Logger.Info("{0} len={1}", name, bodyLen);
        return true;
    }

    /// <summary>
    /// No matching SC opcode in 10.0.2.13 — Zone already applied the break locally; World logs.
    /// </summary>
    private static bool HandleGimmickRemoveParts(PacketStream stream, int bodyLen)
    {
        if (stream.Count < 8)
            return false;
        var gimmickId = stream.ReadUInt32();
        var partCount = stream.ReadUInt32();
        var parts = new uint[Math.Min(partCount, 128)];
        for (var i = 0; i < parts.Length && stream.Count >= 4; i++)
            parts[i] = stream.ReadUInt32();
        Logger.Info("ZWGimmickRemoveParts gimmick={0} count={1} parts=[{2}]",
            gimmickId, partCount, string.Join(",", parts));
        return true;
    }

    /// <summary>
    /// Zone asks World to author a static gimmick and broadcasts the resulting creation state.
    /// </summary>
    private static bool HandleRequestSpawnStaticGimmick(uint ownerZoneId, PacketStream stream, int bodyLen)
    {
        if (stream.LeftBytes < GimmickSpawnData.MinimumSerializedLength)
        {
            Logger.Warn("ZWRequestSpawnStaticGimmick rejected short body len={0}", bodyLen);
            return true;
        }

        try
        {
            if (!GimmickSpawnData.TryRead(stream, out var data) || stream.LeftBytes != 0)
            {
                Logger.Warn(
                    "ZWRequestSpawnStaticGimmick rejected malformed body len={0} trailing={1}",
                    bodyLen, stream.LeftBytes);
                return true;
            }

            var x = AAEmu.Commons.Utils.Helpers.ConvertLongX(data.X);
            var y = AAEmu.Commons.Utils.Helpers.ConvertLongY(data.Y);
            Logger.Info(
                "ZWRequestSpawnStaticGimmick id={0} type={1} zone={2} model={3} pos=({4:F1},{5:F1},{6:F1})",
                data.Id, data.Type, data.StaticZoneId, data.ModelPath, x, y, data.Z);
            WorldIntegration.OnZoneRequestStaticGimmick?.Invoke(ownerZoneId, data);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "ZWRequestSpawnStaticGimmick parse failed len={0}", bodyLen);
        }

        return true;
    }

    private static bool HandleGimmickCollided(PacketStream stream)
    {
        if (stream.Count < 12)
            return false;
        var gimmickId = stream.ReadUInt32();
        var unitId = stream.ReadUInt32();
        var relVel = stream.ReadSingle();
        // No SCGimmickCollided — Zone already reacted (damage/script). World keeps the audit trail.
        Logger.Info("ZWGimmickCollided gimmick={0} unit={1} relVel={2:F2}", gimmickId, unitId, relVel);
        return true;
    }

    private static bool HandleDoodadCollide(PacketStream stream)
    {
        Logger.Info("ZWDoodadCollideUnit len={0}", stream.Count);
        // Physics notify — World may apply knockback SC later; log for now.
        return true;
    }

    private static bool HandleBackpackDropped(PacketStream stream, int bodyLen)
    {
        Logger.Info("ZWBackpackDropped len={0}", bodyLen);
        WorldIntegration.OnZoneBackpackDropped?.Invoke(stream.GetBytes());
        return true;
    }

    private static bool HandleMole(string name, int bodyLen)
    {
        Logger.Warn("{0} len={1} — Zone anti-cheat report (World decides punishment)", name, bodyLen);
        return true;
    }

    private static bool HandleTowerDef(uint zoneId, PacketStream stream)
    {
        if (stream.Count < 10)
            return false;
        var type = stream.ReadUInt32();
        var type2 = stream.ReadUInt16();
        var playable = stream.ReadUInt32();
        TowerDefScheduler.OnPlayabilityReport(zoneId, type, type2, playable);
        return true;
    }

    private static bool HandleResponseCombatUnits(PacketStream stream, int bodyLen)
    {
        Logger.Info("ZWResponseCombatUnits len={0}", bodyLen);
        return true;
    }

    private static bool HandleTimeOfDay(uint zoneId, PacketStream stream, bool detailed)
    {
        if (stream.Count < 4)
            return false;
        var time = stream.ReadSingle();
        var speed = 0.0016666f;
        var start = 0f;
        var end = 24f;
        if (detailed && stream.Count >= 12)
        {
            speed = stream.ReadSingle();
            start = stream.ReadSingle();
            end = stream.ReadSingle();
            Logger.Info(
                "ZWDetailedTimeOfDay zoneId={0} time={1:F2} speed={2:F3} start={3:F2} end={4:F2}",
                zoneId, time, speed, start, end);
        }
        else
            Logger.Info("ZWTimeOfDay zoneId={0} time={1:F2}", zoneId, time);
        WorldIntegration.OnZoneTimeOfDay?.Invoke(zoneId, time, speed, start, end, detailed);
        return true;
    }
}
