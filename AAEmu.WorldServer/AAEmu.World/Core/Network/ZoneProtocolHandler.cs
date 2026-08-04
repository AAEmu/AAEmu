using System;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Packets.Zw;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Network;

public class ZoneProtocolHandler : BaseProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly CombatRelay _combatRelay = new();
    private readonly MovementRelay _movementRelay = new();
    private readonly NpcSpawnRelay _npcSpawnRelay = new();
    private readonly ZoneSimRelay _zoneSimRelay = new();

    public override void OnConnect(ISession session)
    {
        Logger.Info("Zone connect from {0}, session id: {1}", session.Ip, session.SessionId);
        // Do NOT clear NpcStateSent here — ZoneId is unknown until ZWJoin, and wiping
        var connection = new ZoneConnection(session);
        ZoneSession.Instance.Add(connection);
    }

    public override void OnDisconnect(ISession session)
    {
        var connection = ZoneSession.Instance.Get(session.SessionId);
        var zoneId = connection?.ZoneId ?? 0;
        // otherwise sibling zones that later allocate colliding bcIds (pre-fix) or
        // remirror leave ghost units and SC movement thrash.
        if (connection != null)
            NpcSpawnRelay.RemoveMirrorsForConnection(connection, $"zone TCP disconnect {session.Ip}");
        ZoneSession.Instance.Remove(session.SessionId);
        NpcSpawnRelay.ResetNpcStateSentForZone(zoneId, $"zone TCP disconnect {session.Ip}");
        Logger.Error(
            "Zone disconnect from {0} zoneId={1} — returning affected clients to character select",
            session.Ip, zoneId);
        // Recover only clients whose Transform.ZoneId matches; sibling zones stay live.
        AAEmu.Game.WorldIntegration.NotifyZoneLost(
            $"zone TCP disconnect {session.Ip}", zoneId);
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var connection = ZoneSession.Instance.Get(session.SessionId);
        if (connection == null)
        {
            Logger.Error("Zone receive with no connection for session {0}", session.SessionId);
            return;
        }

        try
        {
            ParseFrames(connection, buf, offset, bytes);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Zone protocol error from {0}", connection.Ip);
            connection.Close();
        }
    }

    private void ParseFrames(ZoneConnection connection, byte[] buf, int offset, int bytes)
    {
        var stream = new PacketStream();
        if (connection.LastPacket != null)
        {
            stream.Insert(0, connection.LastPacket);
            connection.LastPacket = null;
        }

        stream.Insert(stream.Count, buf, offset, bytes);

        while (stream is { Count: > 0 })
        {
            ushort len;
            try
            {
                len = stream.ReadUInt16();
            }
            catch (MarshalException)
            {
                stream.Rollback();
                connection.LastPacket = stream;
                return;
            }

            var packetLen = len + stream.Pos;
            if (packetLen > stream.Count)
            {
                stream.Rollback();
                connection.LastPacket = stream;
                return;
            }

            stream.Rollback();
            var frame = new PacketStream();
            frame.Replace(stream, 0, packetLen);

            if (stream.Count > packetLen)
            {
                var remainder = new PacketStream();
                remainder.Replace(stream, packetLen, stream.Count - packetLen);
                stream = remainder;
            }
            else
            {
                stream = null;
            }

            frame.ReadUInt16(); // length
            var opcode = frame.ReadUInt16();
            var bodyLen = packetLen - 4;
            var body = new PacketStream();
            if (bodyLen > 0)
                body.Replace(frame.Buffer, frame.Pos, bodyLen);

            HandleZwPacket(connection, opcode, body, bodyLen);
        }
    }

    private void HandleZwPacket(ZoneConnection connection, ushort opcode, PacketStream body, int bodyLen)
    {
        switch (opcode)
        {
            case ZwOpcodes.Join:
                if (connection.State < ZoneConnectionState.Joined)
                    HandleJoin(connection, body);
                else
                    Logger.Warn("ZW opcode 0 after join from {0} len={1}", connection.Ip, bodyLen);
                break;
            case ZwOpcodes.UnitMovements:
                Logger.Info("ZWUnitMovements from zone {0} len={1}", connection.Ip, bodyLen);
                _movementRelay.RelayZoneMoveToClient(connection, body.GetBytes());
                break;
            case ZwOpcodes.Heartbeat:
                Logger.Debug("ZWHeartbeat from zone {0} (units={1})", connection.Ip, connection.Units.Count);
                break;
            case ZwOpcodes.ZoneLoaded:
                connection.State = ZoneConnectionState.ZoneLoaded;
                ZoneSession.Instance.IndexByZoneId(connection);
                PlayerEnterService.OnZoneLoaded(connection.ZoneId);
                Logger.Info(
                    "Zone {0} ZoneLoaded zoneId={1} instanceId={2} units={3}; registry loadedCount={4}",
                    connection.Ip, connection.ZoneId, connection.InstanceId, connection.Units.Count,
                    ZoneSession.Instance.LoadedCount);
                // have run before Zone connected — flush existing + keep live RelayCreateDoodad.
                WorldIntegration.NotifyZoneReadyForDoodads?.Invoke(connection.ZoneId);
                WorldIntegration.NotifyZoneReadyForHousing?.Invoke(connection.ZoneId);
                WorldIntegration.NotifyZoneReadyForGimmicks?.Invoke(connection.ZoneId);
                // schedule-linked spawners stay held back until the period next reopens.
                GameScheduleRelay.OnZoneLoaded(connection);
                var npcActivate = global::AAEmu.World.WorldRuntime.Config.NpcSpawnerActivate;
                if (npcActivate.PrewarmOnZoneLoaded)
                    ActivateNpcSpawnersForZone(connection);
                else if (npcActivate.Enabled)
                    Logger.Info(
                        "NPC spawners for zoneId={0} will activate around the first entering player (r={1:F0})",
                        connection.ZoneId, npcActivate.Radius);
                break;
            case ZwOpcodes.SpawnNpc:
                HandleSpawnNpc(connection, body);
                break;
            case ZwOpcodes.RemoveNpc:
                HandleRemoveNpc(connection, body);
                break;
            case ZwOpcodes.TimeOfDay:
            case ZwOpcodes.DetailedTimeOfDay:
                if (!_zoneSimRelay.TryHandle(connection, opcode, body.GetBytes(), bodyLen))
                    Logger.Info("ZW TimeOfDay opcode 0x{0:X4} len={1}", opcode, bodyLen);
                break;
            default:
                if (_combatRelay.IsCombatOpcode(opcode))
                    _combatRelay.OnZwOpcode(opcode, body.GetBytes(), bodyLen);
                else if (_zoneSimRelay.TryHandle(connection, opcode, body.GetBytes(), bodyLen))
                {
                    // handled (areas/housing/gimmick/mole/…)
                }
                else
                    Logger.Info("ZW opcode 0x{0:X4} len={1}", opcode, bodyLen);
                break;
        }
    }

    private static void HandleJoin(ZoneConnection connection, PacketStream body)
    {
        var join = ZWJoinPacket.Read(body);
        Logger.Info(
            "ZWJoin from {0}: p_from={1} p_to={2} id={3} ip=0x{4:X8} port={5} accountId={6}",
            connection.Ip, join.PFrom, join.PTo, join.Id, join.Ip, join.Port, join.AccountId);

        // NikES gate: JoinResponse + FactionRelationList + SpawnerList(last=1).
        // Real FromGame() lists are sent by default. An earlier note had them crashing the
        // hostile, and its NPCs aggro but never engage.
        // Opt-out: AAEMU_WZ_REAL_FACTIONS=0 (+ optional WorldGameTime/DetailedToD).
        connection.ZoneId = (uint)join.Id;
        connection.InstanceId = join.InstanceId;
        NpcSpawnRelay.ResetNpcStateSentForZone(connection.ZoneId, $"ZWJoin from {connection.Ip}");
        ZoneSession.Instance.IndexByZoneId(connection);
        var joinResponse = new WZJoinResponsePacket();
        connection.SendPacket(joinResponse);
        Logger.Info("WZJoinResponse → zone {0} fset features: {1}", connection.ZoneId, joinResponse.DescribeFeatures());
        var realFactions = Environment.GetEnvironmentVariable("AAEMU_WZ_REAL_FACTIONS") != "0";
        if (realFactions)
        {
            WZFactionListPacket.SendAllFromGame(connection);
            WZFactionRelationListPacket.SendAllFromGame(connection);
        }
        else
        {
            connection.SendPacket(new WZFactionListPacket());
            connection.SendPacket(new WZFactionRelationListPacket());
        }
        // Saved indun spawner state; empty last=1 for seamless worlds, which is every open-world
        var persistent = ZoneNpcSpawnerCatalog.GetPersistentSpawners(connection.ZoneId, connection.InstanceId);
        WZSpawnerListPacket.SendAll(connection, persistent);
        connection.SendPacket(new WZTimeOfDayPacket(12.0f));
        if (Environment.GetEnvironmentVariable("AAEMU_WZ_WORLD_GAMETIME") == "1")
        {
            var gameTime = (uint)(DateTime.UtcNow.TimeOfDay.TotalSeconds);
            connection.SendPacket(new WZWorldGameTimePacket(gameTime));
            connection.SendPacket(new WZDetailedTimeOfDayPacket(12.0f, 1.0f, 0.0f, 24.0f));
        }
        connection.State = ZoneConnectionState.Joined;
        Logger.Info(
            "Sent bring-online gate (JoinResponse + FactionList/Relations + SpawnerList({0}) + ToD) to {1} zoneId={2} realFactions={3}",
            persistent.Count, connection.Ip, connection.ZoneId, realFactions);
    }

    private static void ActivateNpcSpawnersForZone(ZoneConnection connection)
    {
        var cfg = global::AAEmu.World.WorldRuntime.Config.NpcSpawnerActivate;
        if (!cfg.Enabled)
        {
            Logger.Warn("NpcSpawnerActivate disabled — zone will not populate NPCs until enabled");
            return;
        }

        // Zone can finish load before Game MainWorld exists; mirroring needs MainWorld.
        if (AAEmu.Game.Core.Managers.World.WorldManager.Instance.MainWorld == null)
        {
            Logger.Warn("Deferring WZActivateNpcSpawners for {0} — waiting for MainWorld", connection.Ip);
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < 180; i++)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    if (connection.State < ZoneConnectionState.ZoneLoaded)
                        return;
                    if (AAEmu.Game.Core.Managers.World.WorldManager.Instance.MainWorld != null)
                    {
                        SendActivateNpcSpawners(connection, cfg);
                        return;
                    }
                }

                Logger.Error("Timed out waiting for MainWorld before activating spawners for {0}", connection.Ip);
            });
            return;
        }

        SendActivateNpcSpawners(connection, cfg);
    }

    /// <summary>
    /// Re-announces the activate sphere for a zone that is already loaded. The schedule gate uses
    /// this when a window opens, so placements it suppressed earlier announce themselves again.
    /// </summary>
    internal static void ReactivateNpcSpawners(ZoneConnection connection, string reason)
    {
        var cfg = global::AAEmu.World.WorldRuntime.Config.NpcSpawnerActivate;
        if (!cfg.Enabled)
            return;

        Logger.Info("Re-activating NPC spawners on zoneId={0} — {1}", connection.ZoneId, reason);
        SendActivateNpcSpawners(connection, cfg);
    }

    private static void SendActivateNpcSpawners(ZoneConnection connection, global::AAEmu.World.Models.NpcSpawnerActivateConfig cfg)
    {
        // ZoneId cells — center the activate sphere on that zone or distant starters (Nuian
        // Solzreed / Firran Falcorth / …) get zero ZWSpawnNpc even when ZoneLoaded.
        // The closed set must exist before the ZWSpawnNpc flood it gates, and zone load can beat
        // the refresh timer's first tick.
        NpcScheduleGate.EnsureLoaded();

        ResolveNpcActivateCenter(connection.ZoneId, cfg, out var x, out var y, out var z, out var source, out var radius);
        var local = AAEmu.Game.Core.Managers.World.ZoneManager.Instance.ConvertToLocalCoordinates(
            connection.ZoneId, new System.Numerics.Vector3(x, y, z));
        connection.SendPacket(new WZActivateNpcSpawnersInAreaPacket(local.X, local.Y, local.Z, radius, activate: true));
        Logger.Info(
            "WZActivateNpcSpawnersInArea → zone {0} zoneId={1} local=({2:F1},{3:F1},{4:F1}) world=({5:F1},{6:F1},{7:F1}) r={8:F0} via={9} (expect ZWSpawnNpc flood)",
            connection.Ip, connection.ZoneId, local.X, local.Y, local.Z, x, y, z, radius, source);
    }

    /// <summary>
    /// Prefer zone-group AABB center, else xml world-zone origin cell +1/+1 (typical 2×2 load),
    /// else Config.json fallback. Radius expands to cover the group AABB when larger than config.
    /// </summary>
    private static void ResolveNpcActivateCenter(
        uint zoneId,
        global::AAEmu.World.Models.NpcSpawnerActivateConfig cfg,
        out float x,
        out float y,
        out float z,
        out string source,
        out float radius)
    {
        x = cfg.X;
        y = cfg.Y;
        z = cfg.Z;
        radius = cfg.Radius;
        source = "config";
        if (zoneId == 0)
            return;

        try
        {
            var zone = ZoneManager.Instance.GetZoneByKey(zoneId);
            if (zone != null)
            {
                var group = ZoneManager.Instance.GetZoneGroupById(zone.GroupId);
                if (group is { Width: > 0, Hight: > 0 })
                {
                    x = group.X + group.Width * 0.5f;
                    y = group.Y + group.Hight * 0.5f;
                    var halfDiag = MathF.Sqrt(group.Width * group.Width + group.Hight * group.Hight) * 0.5f;
                    radius = MathF.Max(cfg.Radius, halfDiag + 512f);
                    source = $"zoneGroup:{group.Id}/{group.Name}";
                    return;
                }
            }

            var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
            if (world?.XmlWorldZones != null && world.XmlWorldZones.ContainsKey(zoneId))
            {
                var origin = ZoneManager.Instance.GetZoneOriginCell(zoneId);
                x = (origin.X + 1f) * 1024f;
                y = (origin.Y + 1f) * 1024f;
                source = $"xmlOrigin:({origin.X},{origin.Y})";
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(
                "ResolveNpcActivateCenter zoneId={0} failed ({1}); using Config.json center",
                zoneId, ex.Message);
        }
    }

    private void HandleSpawnNpc(ZoneConnection connection, PacketStream body)
    {
        _npcSpawnRelay.OnSpawn(connection, body.GetBytes());
    }

    private void HandleRemoveNpc(ZoneConnection connection, PacketStream body)
    {
        _npcSpawnRelay.OnRemove(connection, body.GetBytes());
    }
}
