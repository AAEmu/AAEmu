using System;
using System.Collections.Concurrent;

using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// CSMoveUnit → WZUnitMovement and ZWUnitMovements → SCUnitMovements.
/// Local Zone positions are converted at the boundary when local-wire mode is enabled.
/// </summary>
public class MovementRelay
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const int ScMaxUnits = 400;

    /// <summary>
    /// AAEMU_LOG_ZONE_MOVE_POS=1 traces positions received from Zone after any enabled conversion.
    /// </summary>
    private static readonly bool LogMovePositions =
        System.Environment.GetEnvironmentVariable("AAEMU_LOG_ZONE_MOVE_POS") == "1";

    /// <summary>AAEMU_DISABLE_ZONE_SLAVE_POS=1 stops World from tracking dedicate-simulated hulls.</summary>
    private static readonly bool DisableHullPositionSync =
        System.Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_SLAVE_POS") == "1";

    /// <summary>bcId → how many hull position applications have been logged, to keep the trace short.</summary>
    private static readonly ConcurrentDictionary<uint, int> HullSyncLogged = new();

    /// <summary>bcId → template of a CanFly mirror; 0 marks a known non-flier so we look up once.</summary>
    private static readonly ConcurrentDictionary<uint, uint> FlierTemplates = new();
    private static readonly ConcurrentDictionary<uint, float> LastFlierZ = new();

    /// <summary>
    /// Per-unit travel record. Zone streams every unit every tick regardless of whether it moved, so
    /// batch counts say nothing about who is actually walking — only accumulated displacement does.
    /// Drift is first→last distance (a unit that patrols back home ends with drift 0) and Travelled is
    /// the summed step length (which patrol does show), so the pair separates "still" from "wandering".
    /// </summary>
    private sealed class Travel
    {
        public float FirstX, FirstY, LastX, LastY, LastZ;
        public double Travelled;
        public int Samples;
        public uint TemplateId;
        public bool Flier;
    }

    private static readonly ConcurrentDictionary<uint, Travel> Travels = new();
    private static long _nextCensusReport;

    private int _relayLog;

    /// <summary>Accumulates horizontal travel per unit and reports a census every 30s.</summary>
    private static void Census(uint bcId, MoveType mt)
    {
        var travel = Travels.GetOrAdd(bcId, _ =>
        {
            var record = new Travel { FirstX = mt.X, FirstY = mt.Y };
            if (WorldIntegration.FindUnitAcrossWorlds(bcId) is Npc npc)
            {
                record.TemplateId = npc.TemplateId;
                record.Flier = npc.CanFly;
            }

            return record;
        });

        if (travel.Samples > 0)
            travel.Travelled += MathF.Sqrt(((mt.X - travel.LastX) * (mt.X - travel.LastX)) + ((mt.Y - travel.LastY) * (mt.Y - travel.LastY)));

        travel.LastX = mt.X;
        travel.LastY = mt.Y;
        travel.LastZ = mt.Z;
        travel.Samples++;

        var now = System.Environment.TickCount64;
        if (now < _nextCensusReport)
            return;

        _nextCensusReport = now + 30_000;
        ReportCensus();
    }

    private static void ReportCensus()
    {
        var all = Travels.ToArray();
        static float Drift(Travel t) =>
            MathF.Sqrt(((t.LastX - t.FirstX) * (t.LastX - t.FirstX)) + ((t.LastY - t.FirstY) * (t.LastY - t.FirstY)));

        var fliers = all.Where(kv => kv.Value.Flier).ToList();
        Logger.Info(
            "ZWUnitMovements census units={0} driftGt2m={1} travelledGt5m={2} still={3} fliers={4} fliersTravelledGt5m={5}",
            all.Length,
            all.Count(kv => Drift(kv.Value) > 2f),
            all.Count(kv => kv.Value.Travelled > 5),
            all.Count(kv => kv.Value.Travelled <= 1),
            fliers.Count,
            fliers.Count(kv => kv.Value.Travelled > 5));

        foreach (var kv in all.OrderByDescending(kv => kv.Value.Travelled).Take(5))
        {
            Logger.Info(
                "  census top bc={0} tpl={1} fly={2} drift={3:F1}m travelled={4:F1}m samples={5} pos=({6:F1},{7:F1},{8:F1})",
                kv.Key, kv.Value.TemplateId, kv.Value.Flier, Drift(kv.Value), kv.Value.Travelled,
                kv.Value.Samples, kv.Value.LastX, kv.Value.LastY, kv.Value.LastZ);
        }

        foreach (var kv in fliers.OrderByDescending(kv => kv.Value.Travelled).Take(3))
        {
            Logger.Info(
                "  census flier bc={0} tpl={1} drift={2:F1}m travelled={3:F1}m pos=({4:F1},{5:F1},{6:F1})",
                kv.Key, kv.Value.TemplateId, Drift(kv.Value), kv.Value.Travelled,
                kv.Value.LastX, kv.Value.LastY, kv.Value.LastZ);
        }
    }

    /// <summary>
    /// Logs a flier only when its altitude actually changed, so a stable hawk stays silent and a
    /// falling one prints a descending trail.
    /// </summary>
    private static void LogFlierPosition(uint bcId, MoveTypeEnum type, MoveType mt)
    {
        if (!FlierTemplates.TryGetValue(bcId, out var templateId))
        {
            // Not mirrored yet — leave it uncached so the next batch retries.
            if (WorldIntegration.FindUnitAcrossWorlds(bcId) is not Npc npc)
                return;

            templateId = npc.CanFly ? npc.TemplateId : 0u;
            FlierTemplates[bcId] = templateId;
        }

        if (templateId == 0)
            return;

        var dz = LastFlierZ.TryGetValue(bcId, out var lastZ) ? mt.Z - lastZ : 0f;
        if (dz != 0f && MathF.Abs(dz) < 0.5f)
            return;

        LastFlierZ[bcId] = mt.Z;
        Logger.Info(
            "ZWUnitMovements flier bc={0} tpl={1} type={2} pos=({3:F1},{4:F1},{5:F1}) dz={6:F1}",
            bcId, templateId, type, mt.X, mt.Y, mt.Z, dz);
    }

    /// <summary>
    /// Move types whose body carries a position the driving client authored itself. The client reader
    /// which the server authors, and type 5 is the helm request the client sends up.
    /// </summary>
    private static bool IsClientAuthoredVehicleMove(MoveTypeEnum type) =>
        type is MoveTypeEnum.Vehicle or (MoveTypeEnum)3;

    /// <summary>
    /// ObjId of the slave this character is steering, or 0 when it holds no driver seat. Only the
    /// driver's client owns the hull's motion; passengers ride the attachment and still need the
    /// streamed state.
    /// </summary>
    private static uint DrivenSlaveObjId(Character character)
    {
        if (character?.Transform?.Parent?.GameObject is not Slave slave)
            return 0;

        return slave.AttachedCharacters.TryGetValue(AttachPointKind.Driver, out var driver) &&
               driver?.ObjId == character.ObjId
            ? slave.ObjId
            : 0;
    }

    /// <summary>
    /// True when this connection is the dedicate the hull was handed to (or ownership is unknown).
    /// </summary>
    private static bool OwnsHull(ZoneConnection source, uint bcId)
    {
        if (source == null)
            return true;

        if (WorldIntegration.FindUnitAcrossWorlds(bcId) is not Slave slave || slave.ZoneAnnouncedTo == 0)
            return true;

        return source.ZoneId == slave.ZoneAnnouncedTo;
    }

    /// <summary>
    /// True when this connection is the newly armed simulator World has not started following yet.
    /// </summary>
    private static bool IsSeamWarmup(ZoneConnection? source, uint bcId)
    {
        if (source == null)
            return false;

        if (WorldIntegration.FindUnitAcrossWorlds(bcId) is not Slave slave)
            return false;

        return BoatZoneSimRules.IsWarmupSource(source.ZoneId, slave.ZoneAnnouncedTo, slave.ZoneSimPendingFor);
    }

    /// <summary>Copies zone-owned NPC and mate positions onto their World mirrors.</summary>
    private static void ApplyCombatUnitPosition(uint bcId, UnitMoveType move, ZoneConnection source)
    {
        if (DisableHullPositionSync)
            return;

        var unit = WorldIntegration.FindUnitAcrossWorlds(bcId);
        if (unit is not Npc && unit is not Mate)
            return;

        // Reject movement from a zone that does not own this unit.
        var unitZone = unit.Transform?.ZoneId ?? 0;
        if (source != null && unitZone != 0 && source.ZoneId != unitZone)
            return;

        // Dedic owns pathing/flight — World only mirrors. Never clamp mid-path jumps (rift fly-ins
        // span hundreds of metres). Only repair multi-km snaps that look like raw zone-local on a
        // world-space mirror (ObjectId recycle / space poisoning), by converting local→world when
        // that lands much closer to the last known world position.
        if (unit is Npc { IsZoneMirror: true } && unitZone != 0)
        {
            var cur = unit.Transform.World.Position;
            var raw = new System.Numerics.Vector3(move.X, move.Y, move.Z);
            var dx = raw.X - cur.X;
            var dy = raw.Y - cur.Y;
            var dz = raw.Z - cur.Z;
            var rawD2 = dx * dx + dy * dy + dz * dz;
            const float poisonJumpM = 2000f;
            if (rawD2 > poisonJumpM * poisonJumpM)
            {
                var asWorld = ZoneManager.Instance.ConvertToWorldCoordinates(unitZone, raw);
                var wx = asWorld.X - cur.X;
                var wy = asWorld.Y - cur.Y;
                var wz = asWorld.Z - cur.Z;
                var worldD2 = wx * wx + wy * wy + wz * wz;
                if (worldD2 < rawD2 * 0.25f)
                {
                    if (LogMovePositions)
                    {
                        Logger.Warn(
                            "ZW move local→world bc={0} jump={1:F0}m raw=({2:F1},{3:F1}) → world=({4:F1},{5:F1}) zoneId={6}",
                            bcId, MathF.Sqrt(rawD2), raw.X, raw.Y, asWorld.X, asWorld.Y, unitZone);
                    }

                    move.X = asWorld.X;
                    move.Y = asWorld.Y;
                    move.Z = asWorld.Z;
                }
            }
        }

        unit.Transform.Local.SetPosition(
            move.X, move.Y, move.Z,
            (float)MathUtil.ConvertDirectionToRadian(move.RotationX),
            (float)MathUtil.ConvertDirectionToRadian(move.RotationY),
            (float)MathUtil.ConvertDirectionToRadian(move.RotationZ));
        unit.Transform.FinalizeTransform();
        NpcHeightDiagnostics.ObserveMove(bcId, move.X, move.Y, move.Z);
    }

    /// <summary>
    /// Copies a dedicate-simulated hull position onto the World mirror. CSMoveUnit for a ship only
    /// carries throttle and steering, and nothing else writes a Slave transform under ZoneAuthority,
    /// so without this the hull stays at its summon point for the whole voyage — and so does every
    /// character parented to it, because their local offset is 0. Everything World owns off the
    /// player's position then reads the dock: sphere areas (Ezi's Divine Protection never expires),
    /// region interest, quest areas, and the logout position.
    /// </summary>
    private static void ApplyHullPosition(uint bcId, ShipMoveType ship, ZoneConnection source)
    {
        if (DisableHullPositionSync)
            return;

        if (WorldIntegration.FindUnitAcrossWorlds(bcId) is not Slave slave)
            return;

        // A hull belongs to exactly one dedicate. A stale one that was never told to drop it keeps
        // simulating the ship, and letting both write here made the mirror alternate between two
        // positions and headings every tick.
        if (source != null && slave.ZoneAnnouncedTo != 0 && source.ZoneId != slave.ZoneAnnouncedTo)
            return;

        var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationInDegrees(ship.RotationX, ship.RotationY, ship.RotationZ);
        var before = slave.Transform.World.ClonePosition();
        slave.Transform.Local.SetPosition(ship.X, ship.Y, ship.Z, rotX, rotY, rotZ);
        slave.Transform.FinalizeTransform();
        slave.Throttle = ship.Throttle;
        slave.Steering = ship.Steering;
        if (slave.SimulatedShipState != null && slave.SimulatedShipStateAtMs != 0)
        {
            slave.PreviousSimulatedShipState = slave.SimulatedShipState;
            slave.PreviousSimulatedShipStateAtMs = slave.SimulatedShipStateAtMs;
        }

        slave.SimulatedShipState = ship;
        slave.SimulatedShipStateAtMs = Environment.TickCount64;
        MeasureHullSpeed(bcId, source?.ZoneId ?? slave.ZoneAnnouncedTo, slave, ship);
        SlaveManager.TryRecoverBoatWaterline(slave);

        var logged = HullSyncLogged.AddOrUpdate(bcId, 1, (_, count) => count + 1);
        if (logged <= 3 || logged % 600 == 0)
        {
            Logger.Info(
                "Hull position from zone bc={0} {1} ({2:F1},{3:F1},{4:F1}) → ({5:F1},{6:F1},{7:F1}) zone={8}",
                bcId, slave.Name, before.X, before.Y, before.Z, ship.X, ship.Y, ship.Z, slave.Transform.ZoneId);
        }
    }

    /// <summary>
    /// Keeps the speed the hull is actually making on the hull, and reports it when it is beyond what
    /// its own thrust can reach. See <see cref="HullSpeedMonitor"/>.
    /// </summary>
    private static void MeasureHullSpeed(uint bcId, uint zoneId, Slave slave, ShipMoveType ship)
    {
        var now = Environment.TickCount64;
        if (HullSpeedMonitor.Observe(bcId, zoneId, ship.X, ship.Y, ship.Z, now) is not { } speed)
            return;

        slave.SimulatedSpeed = speed;
        slave.SimulatedSpeedAtMs = now;

        // Judge the hull against what it can actually make with its rig, not its bare model figure:
        // sails carry large max-speed multipliers, so the bare figure reports normal sailing as a fault.
        var maxVelocity = ShipPoseSeed.EffectiveMaxVelocity(slave);

        // The first pose the new simulator publishes is what says how much way survived the handover, so
        // the correction is measured against it rather than guessed at arm time.
        SlaveManager.ApplySeamSpeedCorrection(slave, zoneId, ship.ReportedSpeed);

        if (slave.SeamSpeedProbes > 0)
        {
            var sample = SlaveManager.SeamSpeedProbeCount - slave.SeamSpeedProbes + 1;
            slave.SeamSpeedProbes--;
            Logger.Info(
                "Seam speed probe obj={0} zone={1} sample={2}/{3} speed={4:F1} m/s reportedVel={5:F1} " +
                "m/s throttle={6} steering={7}",
                slave.ObjId, slave.ZoneAnnouncedTo, sample, SlaveManager.SeamSpeedProbeCount, speed,
                ship.ReportedSpeed, ship.Throttle, ship.Steering);
        }

        if (!HullSpeedMonitor.IsOverspeed(speed, maxVelocity) || !HullSpeedMonitor.ShouldReport(bcId, now))
            return;

        Logger.Warn(
            "Hull faster than its rig allows bc={0} {1} {2:F1} m/s (max {3:F1}) throttle={4} " +
            "vel=({5},{6},{7}) zone={8}",
            bcId, slave.Name, speed, maxVelocity, ship.Throttle, ship.VelX, ship.VelY, ship.VelZ,
            slave.ZoneAnnouncedTo);
    }

    /// <summary>
    /// Diagnostic (<c>AAEMU_LOG_SEAM_STREAM=1</c>): every hull body written to clients while the
    /// hull is within <see cref="SeamStreamLogWindowMs"/> of an arm. Raw → pinned fields show what
    /// the client interpolator is handed across a follow switch.
    /// </summary>
    private static readonly bool LogSeamStreamEnabled =
        Environment.GetEnvironmentVariable("AAEMU_LOG_SEAM_STREAM") == "1";

    private const long SeamStreamLogWindowMs = 8000;

    private static void LogSeamStream(
        Slave slave, ShipMoveType pinned, ZoneConnection source, ushort rawZone, uint rawTime, sbyte rawSteer)
    {
        if (!LogSeamStreamEnabled || slave.SeamArmedAtMs == 0)
            return;
        var sinceArm = Environment.TickCount64 - slave.SeamArmedAtMs;
        if (sinceArm > SeamStreamLogWindowMs)
            return;

        var (_, _, yaw) = MathUtil.GetSlaveRotationInDegrees(pinned.RotationX, pinned.RotationY, pinned.RotationZ);
        Logger.Info(
            "Seam stream obj={0} src={1} +{2}ms pos=({3:F2},{4:F2},{5:F2}) yaw={6:F1} vel=({7:F2},{8:F2},{9:F2}) " +
            "steer={10}→{11} thr={12} time={13}→{14} zoneId={15}→{16} follow={17} pending={18}",
            slave.ObjId, source?.ZoneId ?? 0, sinceArm,
            pinned.X, pinned.Y, pinned.Z, yaw, pinned.VelX, pinned.VelY, pinned.VelZ,
            rawSteer, pinned.Steering, pinned.Throttle, rawTime, pinned.Time, rawZone, pinned.ZoneId,
            slave.ZoneAnnouncedTo, slave.ZoneSimPendingFor);
    }

    /// <summary>
    /// Follow-switch blend: for <see cref="BoatSeamBlendRules.BlendMs"/> after the switch the
    /// streamed position/yaw start on the outgoing body's track and converge onto the incoming
    /// body. Returns the zone's own pose so the caller can restore the mirror after the write.
    /// </summary>
    private static (float X, float Y, float Z, short RX, short RY, short RZ)? BlendStreamedHullPose(
        ShipMoveType hull, Slave slave)
    {
        if (slave.SeamBlendStartMs == 0)
            return null;

        var now = Environment.TickCount64;
        var age = now - slave.SeamBlendStartMs;
        if (!BoatSeamBlendRules.IsActive(age))
        {
            slave.SeamBlendStartMs = 0;
            slave.SeamBlendOffset = null;
            slave.SeamBlendFrom = null;
            return null;
        }

        var (_, _, toYaw) = MathUtil.GetSlaveRotationInDegrees(hull.RotationX, hull.RotationY, hull.RotationZ);
        if (slave.SeamBlendOffset == null)
        {
            if (slave.SeamBlendFrom is not { } from)
            {
                slave.SeamBlendStartMs = 0;
                return null;
            }

            // Where the outgoing track is right now, against the incoming body's first report.
            var dt = Math.Clamp(now - slave.SeamBlendFromAtMs, 0, BoatSeamPredictRules.MaxPredictAgeMs) / 1000f;
            var fromX = from.X + BoatSeamPredictRules.DecodeVelMetresPerSecond(from.VelX) * dt;
            var fromY = from.Y + BoatSeamPredictRules.DecodeVelMetresPerSecond(from.VelY) * dt;
            var (_, _, fromYaw) = MathUtil.GetSlaveRotationInDegrees(from.RotationX, from.RotationY, from.RotationZ);
            slave.SeamBlendOffset = BoatSeamBlendRules.Residual(fromX, fromY, from.Z, fromYaw, hull.X, hull.Y, hull.Z, toYaw);
            slave.SeamBlendFrom = null;
            if (slave.SeamBlendOffset is not { } first)
            {
                slave.SeamBlendStartMs = 0;
                return null;
            }

            Logger.Info(
                "Seam blend obj={0} zone={1} residual=({2:F2},{3:F2},{4:F2}) yaw={5:F1}° over {6} ms",
                slave.ObjId, slave.ZoneAnnouncedTo, first.X, first.Y, first.Z, first.YawDegrees, BoatSeamBlendRules.BlendMs);
        }

        var offset = slave.SeamBlendOffset.Value;
        var w = BoatSeamBlendRules.Weight(age);
        var restore = (hull.X, hull.Y, hull.Z, hull.RotationX, hull.RotationY, hull.RotationZ);
        var (roll, pitch, yaw) = MathUtil.GetSlaveRotationInDegrees(hull.RotationX, hull.RotationY, hull.RotationZ);
        hull.X += offset.X * w;
        hull.Y += offset.Y * w;
        hull.Z += offset.Z * w;
        (hull.RotationX, hull.RotationY, hull.RotationZ) =
            MathUtil.GetSlaveRotationFromDegrees(roll, pitch, yaw + offset.YawDegrees * w);
        return restore;
    }

    /// <summary>
    /// Pins zone id, time and steering on the body that will be written to SC. The World
    /// mirror is restored after that write so seeds still see the zone's own report.
    /// </summary>
    private static void PinStreamedHullVisual(ShipMoveType hull, Slave slave)
    {
        var now = Environment.TickCount64;
        var last = new BoatRudderSeamRules.StreamedShipVisual(
            slave.StreamedShipZoneId, slave.StreamedShipTime, slave.StreamedShipSteering, slave.StreamedShipTimeOffset);
        var elapsedMs = slave.StreamedShipAtMs == 0 ? 0 : now - slave.StreamedShipAtMs;
        var pinned = BoatRudderSeamRules.Pin(
            last, hull.ZoneId, hull.Time, hull.Steering, slave.SteeringRequest, elapsedMs);
        hull.ZoneId = pinned.ZoneId;
        hull.Time = pinned.Time;
        hull.Steering = pinned.Steering;
        slave.StreamedShipZoneId = pinned.ZoneId;
        slave.StreamedShipTime = pinned.Time;
        slave.StreamedShipSteering = pinned.Steering;
        slave.StreamedShipTimeOffset = pinned.TimeOffset;
        slave.StreamedShipAtMs = now;
    }

    public void RelayClientMoveToZone(ZoneConnection zone, uint bcId, byte[] payload)
    {
        if (zone == null || payload == null || payload.Length == 0)
            return;

        var body = payload;
        if (ZoneCoordBoundary.UseLocalOnZoneWire && zone.ZoneId != 0)
        {
            try
            {
                var stream = new PacketStream();
                stream.Insert(0, payload);
                stream.Pos = 0;
                var typeByte = stream.ReadByte();
                var move = MoveType.GetType((MoveTypeEnum)typeByte);
                move.Read(stream);
                ZoneCoordBoundary.ShiftWorldToLocal(zone.ZoneId, move);

                var rewritten = new PacketStream();
                rewritten.Write(typeByte);
                move.Write(rewritten);
                body = rewritten.GetBytes();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "RelayClientMoveToZone local conversion failed bcId={0}; forwarding original payload", bcId);
                body = payload;
            }
        }

        Logger.Debug("RelayClientMoveToZone bcId={0} len={1}", bcId, body.Length);
        zone.SendPacket(new WZUnitMovementPacket(bcId, body));
    }

    public void RelayZoneMoveToClient(ZoneConnection source, byte[] payload)
    {
        if (payload == null || payload.Length < 4)
            return;

        // Zone→client movement relay. Commercial path: ZWUnitMovements (0x08) for units that actually move.
        // Set AAEMU_DISABLE_ZONE_MOVE_RELAY=1 to suppress while validating NPC SCUnitState alone.
        // Never synthesize idle stands (MirrorMovementStream) — those quit the client.
        if (System.Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_MOVE_RELAY") == "1")
            return;

        try
        {
            var stream = new PacketStream();
            stream.Insert(0, payload);
            stream.Pos = 0;
            var count = stream.ReadInt32();
            if (count <= 0)
                return;

            var zoneId = source?.ZoneId ?? 0;

            // Parse and normalize each entry once, then filter it for each client below.
            var entries = new List<(uint BcId, MoveTypeEnum Type, byte[] Body)>(Math.Min(count, ScMaxUnits));
            Dictionary<uint, float> tracedZ = null;
            for (var i = 0; i < count; i++)
            {
                var start = stream.Pos;
                var bcId = stream.ReadBc();
                var typeByte = stream.ReadByte();
                var mt = MoveType.GetType((MoveTypeEnum)typeByte);
                mt.Read(stream);
                var len = stream.Pos - start;
                if (len <= 0 || start + len > payload.Length)
                {
                    Logger.Warn("ZWUnitMovements parse failed at entry {0}/{1} pos={2}", i, count, start);
                    break;
                }

                var localSim = ZoneCoordBoundary.UseLocalOnZoneWire
                    || WorldIntegration.FindUnitAcrossWorlds(bcId) is Npc { ZoneSimUsesLocalCoordinates: true };
                ZoneCoordBoundary.ShiftLocalToWorld(zoneId, mt, localSim);

                var restoreHullVisual = false;
                ushort restoreZone = 0;
                uint restoreTime = 0;
                sbyte restoreSteer = 0;
                (float X, float Y, float Z, short RX, short RY, short RZ)? restorePose = null;

                if (mt is ShipMoveType hull)
                {
                    // Overlap: B's warmup is observed and never streamed. A stays the client body.
                    // Replacing A with ForBridge (frozen plant) was the 1 s stop and the 186→149 fight.
                    if (IsSeamWarmup(source, bcId))
                    {
                        if (WorldIntegration.FindUnitAcrossWorlds(bcId) is Slave warmup)
                        {
                            SlaveManager.ObserveSeamWarmupPose(
                                warmup, source.ZoneId, hull.ReportedSpeed, hull.X, hull.Y);
                        }

                        // The report that made follow switch is B's first body the client should
                        // see. Dropping it left a hole of one B tick plus the two zones' phase
                        // difference (60–120 ms) right at the switch. If B now owns the hull,
                        // fall through and stream this body.
                        if (!OwnsHull(source, bcId))
                            continue;
                    }
                    else if (!OwnsHull(source, bcId))
                    {
                        continue;
                    }

                    ApplyHullPosition(bcId, hull, source);
                    if (WorldIntegration.FindUnitAcrossWorlds(bcId) is Slave live)
                    {
                        SlaveManager.TrackIncomingSeam(live);
                        SlaveManager.TickSeamOverlap(live);
                        restoreZone = hull.ZoneId;
                        restoreTime = hull.Time;
                        restoreSteer = hull.Steering;
                        PinStreamedHullVisual(hull, live);
                        restoreHullVisual = true;
                        restorePose = BlendStreamedHullPose(hull, live);
                        LogSeamStream(live, hull, source, restoreZone, restoreTime, restoreSteer);
                    }
                }
                else if (mt is UnitMoveType unitMove)
                {
                    // NPC / mate World mirrors lagged ZWUnitMovements so Skill.Use range and mate
                    // chase measured stale centers (TooFarRange 5–9 m). Hulls use ApplyHullPosition.
                    ApplyCombatUnitPosition(bcId, unitMove, source);
                }

                if (LogMovePositions)
                {
                    if (_relayLog == 0 && i < 3)
                    {
                        Logger.Info(
                            "ZWUnitMovements pos bc={0} type={1} pos=({2:F1},{3:F1},{4:F1}) zoneId={5} localWire={6}",
                            bcId, (MoveTypeEnum)typeByte, mt.X, mt.Y, mt.Z, zoneId,
                            ZoneCoordBoundary.UseLocalOnZoneWire);
                    }

                    LogFlierPosition(bcId, (MoveTypeEnum)typeByte, mt);
                    Census(bcId, mt);
                }

                var rewritten = new PacketStream();
                rewritten.WriteBc(bcId);
                rewritten.Write(typeByte);
                mt.Write(rewritten);
                if (restoreHullVisual && mt is ShipMoveType streamed)
                {
                    streamed.ZoneId = restoreZone;
                    streamed.Time = restoreTime;
                    streamed.Steering = restoreSteer;
                    if (restorePose is { } pose)
                    {
                        streamed.X = pose.X;
                        streamed.Y = pose.Y;
                        streamed.Z = pose.Z;
                        streamed.RotationX = pose.RX;
                        streamed.RotationY = pose.RY;
                        streamed.RotationZ = pose.RZ;
                    }
                }

                entries.Add((bcId, (MoveTypeEnum)typeByte, rewritten.GetBytes()));
                if (NpcHeightDiagnostics.IsTracing(bcId))
                    (tracedZ ??= [])[bcId] = mt.Z;
            }

            if (entries.Count == 0)
                return;

            // SCUnitMovements max 400 — split if needed (zone buffer holds up to 1000).
            var sentClients = 0;
            WorldIntegration.ForEachReadyConnection((connection, character) =>
            {
                try
                {
                    // A client integrates its own character locally and reports the finished state
                    // through CSMoveUnit, so the zone's copy of it is not streamed back.
                    //
                    // The vehicle it steers is only suppressed for the wheeled types (2 and 3), where
                    // VehicleMoveType carries the position the client itself authored and echoing the
                    // zone's copy would fight it. A ship is the opposite: ShipRequestMoveType holds
                    // nothing but throttle and steering, so the driver has asked the server where the
                    // hull goes and has no answer until the Ship body comes back. Filtering type 4
                    // here leaves the helm live and the boat motionless for whoever is steering it.
                    var ownObjId = character.ObjId;
                    var drivenObjId = DrivenSlaveObjId(character);

                    var visible = entries
                        .Where(entry => entry.BcId != ownObjId)
                        .Where(entry => entry.BcId != drivenObjId || !IsClientAuthoredVehicleMove(entry.Type))
                        .Where(entry => WorldIntegration.IsStreamedUnitForClient(character, entry.BcId))
                        .ToList();

                    if (tracedZ != null)
                    {
                        foreach (var (bcId, z) in tracedZ)
                        {
                            NpcHeightDiagnostics.RecordRelay(
                                bcId, character.Name, z, visible.Any(entry => entry.BcId == bcId));
                        }
                    }

                    if (visible.Count == 0)
                        return;

                    for (var offset = 0; offset < visible.Count; offset += ScMaxUnits)
                    {
                        var n = Math.Min(ScMaxUnits, visible.Count - offset);
                        var sc = new PacketStream();
                        sc.Write((ushort)n);
                        for (var j = 0; j < n; j++)
                            sc.Write(visible[offset + j].Body, false);
                        connection.SendPacket(new SCOpaquePacket(SCOffsets.SCUnitMovementsPacket, sc.GetBytes()));
                    }

                    sentClients++;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "ZWUnitMovements relay failed for connection {0}", connection.Id);
                }
            });

            if (_relayLog < 5 || _relayLog % 200 == 0)
            {
                Logger.Info(
                    "ZWUnitMovements → SCUnitMovements zoneCount={0} parsed={1} clients={2} (per-client AOI)",
                    count, entries.Count, sentClients);
            }

            _relayLog++;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "RelayZoneMoveToClient failed len={0}", payload.Length);
        }
    }
}
