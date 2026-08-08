using System.Collections.Concurrent;

using AAEmu.Game;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Track ZWSpawnNpc / ZWRemoveNpc and mirror into Game for SCUnitState visibility.
/// After a successful mirror, reply with WZNpcState so Zone runs NpcManager::Create + CreateAI
/// </summary>
public class NpcSpawnRelay
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Per-(zoneId, bcId) markers so multi-dedicate does not share Create state.
    /// Clear only the disconnecting / re-joining ZoneId — never wipe sibling zones.
    /// </summary>
    private static readonly ConcurrentDictionary<ulong, byte> NpcStateSent = new();

    private int _hexDumps;
    private int _mirrorOk;
    private int _mirrorFail;
    private int _npcStateOk;
    private int _npcStateFail;
    private int _npcStateSkipped;
    private int _flyStateOk;

    private static readonly bool DisableFlyState =
        Environment.GetEnvironmentVariable("AAEMU_DISABLE_WZ_FLY_STATE") == "1";

    private static ulong MarkerKey(uint zoneId, uint bcId) => ((ulong)zoneId << 32) | bcId;

    /// <summary>
    /// Drop WZNpcState markers for one zone key (Zone disconnect / ZWJoin remap).
    /// </summary>
    public static void ResetNpcStateSentForZone(uint zoneId, string reason)
    {
        if (zoneId == 0)
        {
            Logger.Debug("NpcStateSent skip clear — zoneId=0 ({0})", reason);
            return;
        }

        var removed = 0;
        foreach (var key in NpcStateSent.Keys)
        {
            if ((uint)(key >> 32) != zoneId)
                continue;
            if (NpcStateSent.TryRemove(key, out _))
                removed++;
        }

        Logger.Info("NpcStateSent cleared zoneId={0} entries={1} — {2}", zoneId, removed, reason);
    }

    /// <summary>
    /// Drops one unit's WZNpcState marker so a later announcement of the same bcId is acknowledged
    /// again. Used when the schedule gate retires an NPC whose window closed.
    /// </summary>
    internal static void ForgetNpcState(uint zoneId, uint bcId)
    {
        NpcStateSent.TryRemove(MarkerKey(zoneId, bcId), out _);
    }

    /// <summary>
    /// Delete Game NPC mirrors for this ZoneConnection. Call on TCP disconnect before
    /// removing the connection from ZoneSession.
    /// Players are also tracked via <c>RegisterWithId</c> — only remove entries whose
    /// body parses as <c>ZWSpawnNpc</c> (NPC mirrors), not player WZUnitState blobs.
    /// </summary>
    public static void RemoveMirrorsForConnection(ZoneConnection connection, string reason)
    {
        if (connection == null)
            return;

        var snap = connection.Units.Snapshot();
        var removed = 0;
        var skippedPlayer = 0;
        foreach (var (bcId, raw) in snap)
        {
            NpcStateSent.TryRemove(MarkerKey(connection.ZoneId, bcId), out _);

            // Player RegisterWithId stores WZUnitState bodies. World-authored dynamic NPCs store
            // their WZNpcState body, while Zone-authored mirrors store ZWSpawnNpc.
            if (ZwSpawnNpcParser.TryParse(raw) == null)
            {
                if (WorldIntegration.FindUnitAcrossWorlds(bcId) is Npc { IsZoneMirror: true })
                {
                    WorldIntegration.OnZoneNpcRemove?.Invoke(bcId);
                    ObjectIdManager.Instance.ReleaseId(bcId);
                    removed++;
                }
                else
                {
                    skippedPlayer++;
                }
                connection.Units.TryRemove(bcId);
                continue;
            }

            WorldIntegration.OnZoneNpcRemove?.Invoke(bcId);
            ObjectIdManager.Instance.ReleaseId(bcId);
            removed++;
            connection.Units.TryRemove(bcId);
        }

        Logger.Info(
            "Removed zone mirrors zoneId={0} ip={1} npcs={2} skippedPlayerOrLow={3} — {4}",
            connection.ZoneId, connection.Ip, removed, skippedPlayer, reason);
    }

    public uint OnSpawn(ZoneConnection connection, byte[] raw)
    {
        var parsed = ZwSpawnNpcParser.TryParse(raw);

        // Reject before a bcId is spent. Leaving the announcement unacknowledged keeps Zone from
        // running NpcManager::Create, so the NPC exists nowhere.
        if (parsed != null && NpcScheduleGate.IsClosed(parsed.SpawnerType))
        {
            // WZNpcSpawnFailed has the identical SpawnNpc payload serializer, so echo the exact
            // announcement rather than reconstructing variable strings/creator/reason fields.
            connection.SendPacket(new WZNpcSpawnFailedPacket(raw));
            NpcScheduleGate.CountSuppressed(
                connection.ZoneId, parsed.SpawnerId, parsed.SpawnerType, parsed.TemplateId);
            return 0;
        }

        var bcId = connection.Units.Register(raw);
        var count = connection.Units.Count;

        if (_hexDumps < 3)
        {
            _hexDumps++;
            Logger.Info("ZWSpawnNpc HEX#{0} len={1} {2}", _hexDumps, raw.Length, BitConverter.ToString(raw));
            if (parsed != null)
            {
                Logger.Info(
                    "ZWSpawnNpc PARSE#{0} sid={1} sType={2} tpl={3} pos=({4:F1},{5:F1},{6:F1}) zRot={7:F2} scale={8:F2} → bc={9}",
                    _hexDumps, parsed.SpawnerId, parsed.SpawnerType, parsed.TemplateId,
                    parsed.X, parsed.Y, parsed.Z, parsed.ZRot, parsed.Scale, bcId);
            }
            else
            {
                Logger.Warn("ZWSpawnNpc PARSE#{0} failed", _hexDumps);
            }
        }

        if (parsed != null)
        {
            if (TryMirror(connection.ZoneId, bcId, parsed))
            {
                if (TrySendNpcState(connection, bcId, parsed))
                    ZoneNpcSpawnerCatalog.SetState(connection, parsed, active: true);
            }
            else
                _mirrorFail++;
        }
        else
            _mirrorFail++;

        if (count <= 5 || count % 100 == 0)
        {
            Logger.Info(
                "ZWSpawnNpc #{0} bc={1} tpl={2} mirroredOk={3} fail={4} wzNpcStateOk={5} fail={6}",
                count, bcId, parsed?.TemplateId ?? 0, _mirrorOk, _mirrorFail, _npcStateOk, _npcStateFail);
        }

        return bcId;
    }

    /// <summary>
    /// Re-apply Game mirrors for units already tracked (e.g. zone flooded before MainWorld).
    /// Does not re-register bcIds — reuses existing registry entries that parse as ZWSpawnNpc.
    /// Also sends any missing WZNpcState replies.
    /// </summary>
    public int RemirrorAll(ZoneConnection connection)
    {
        var ok = 0;
        var fail = 0;
        foreach (var (bcId, raw) in connection.Units.Snapshot())
        {
            var parsed = ZwSpawnNpcParser.TryParse(raw);
            if (parsed == null)
            {
                // Dynamic World-authored NPC: its registry value is already the WZNpcState body.
                // Characters are deliberately ignored here.
                if (WorldIntegration.FindUnitAcrossWorlds(bcId) is Npc { IsZoneMirror: true })
                {
                    connection.SendPacket(new WZNpcStatePacket(raw));
                    SyncNpcFactionToZone(connection, bcId);
                    ok++;
                }
                continue;
            }

            // A window can close between the original mirror and a remirror pass.
            if (NpcScheduleGate.IsClosed(parsed.SpawnerType))
                continue;

            if (TryMirror(connection.ZoneId, bcId, parsed))
            {
                ok++;
                if (TrySendNpcState(connection, bcId, parsed))
                    ZoneNpcSpawnerCatalog.SetState(connection, parsed, active: true);
            }
            else
                fail++;
        }

        Logger.Info("RemirrorAll zone {0} zoneId={1} ok={2} fail={3} tracked={4} wzNpcStateOk={5}",
            connection.Ip, connection.ZoneId, ok, fail, connection.Units.Count, _npcStateOk);
        return ok;
    }

    public static void RemirrorAllZones()
    {
        var relay = new NpcSpawnRelay();
        foreach (var zone in ZoneSession.Instance.All)
            relay.RemirrorAll(zone);
    }

    private bool TryMirror(uint zoneId, uint bcId, ZwSpawnNpcParsed parsed)
    {
        try
        {
            var mirrored = WorldIntegration.OnZoneNpcSpawn?.Invoke(
                zoneId, bcId, parsed.TemplateId, parsed.X, parsed.Y, parsed.Z, parsed.ZRot, parsed.Scale) ?? false;
            if (mirrored)
            {
                _mirrorOk++;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "OnZoneNpcSpawn failed bc={0} tpl={1}", bcId, parsed.TemplateId);
            return false;
        }
    }

    private bool TrySendNpcState(ZoneConnection connection, uint bcId, ZwSpawnNpcParsed parsed)
    {
        var key = MarkerKey(connection.ZoneId, bcId);
        if (!NpcStateSent.TryAdd(key, 0))
        {
            var skipped = ++_npcStateSkipped;
            if (skipped <= 5 || skipped % 50 == 0)
            {
                Logger.Info(
                    "WZNpcState SKIPPED #{0} bc={1} tpl={2} zoneId={3} — Create marker already set",
                    skipped, bcId, parsed.TemplateId, connection.ZoneId);
            }

            return true;
        }

        try
        {
            var body = WorldIntegration.BuildWzNpcStateBody(
                bcId,
                parsed.SpawnerId,
                parsed.MemberIdx,
                parsed.PartIdx,
                parsed.TableIdx,
                parsed.GroupType,
                parsed.GroupId,
                parsed.GroupMemberIdx,
                parsed.X,
                parsed.Y,
                parsed.Z);

            if (body == null || body.Length == 0)
            {
                NpcStateSent.TryRemove(key, out _);
                _npcStateFail++;
                return false;
            }

            connection.SendPacket(new WZNpcStatePacket(body));
            _npcStateOk++;
            // Faction is synchronized separately from the initial unit state.
            SyncNpcFactionToZone(connection, bcId);
            TrySendFlyingState(connection, bcId, parsed.TemplateId);

            if (_npcStateOk <= 3 || _npcStateOk % 100 == 0)
            {
                Logger.Info(
                    "WZNpcState #{0} → zone {1} zoneId={2} bc={3} tpl={4} sid={5} bodyLen={6} " +
                    "createLocal=({7:F1},{8:F1},{9:F1}) localWire={10}",
                    _npcStateOk, connection.Ip, connection.ZoneId, bcId, parsed.TemplateId, parsed.SpawnerId,
                    body.Length, parsed.X, parsed.Y, parsed.Z,
                    ZoneCoordBoundary.UseLocalOnZoneWire);
            }

            return true;
        }
        catch (Exception ex)
        {
            NpcStateSent.TryRemove(key, out _);
            _npcStateFail++;
            Logger.Warn(ex, "WZNpcState failed bc={0} tpl={1}", bcId, parsed.TemplateId);
            return false;
        }
    }

    /// <summary>Synchronizes an NPC faction immediately after creating its zone unit.</summary>
    private static void SyncNpcFactionToZone(ZoneConnection connection, uint bcId)
    {
        try
        {
            if (WorldIntegration.FindUnitAcrossWorlds(bcId) is not Npc npc ||
                npc.Faction is null || (uint)npc.Faction.Id == 0)
                return;

            connection.SendPacket(new WZUnitFactionChangedPacket(bcId, 0, (int)npc.Faction.Id, false));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "WZUnitFactionChanged (npc) failed bc={0}", bcId);
        }
    }

    /// <summary>
    /// Birds and other fliers only hold altitude once Zone knows they fly: the flag reaches
    /// CryAI as bFlyingCreature / b3DMove, and until then living-entity physics free-falls them
    /// (observed on Falcony hawks — Z dropped at ~9.8 m/s² and snapped back to spawn on a loop).
    /// </summary>
    private void TrySendFlyingState(ZoneConnection connection, uint bcId, uint templateId)
    {
        if (DisableFlyState)
            return;

        try
        {
            if (WorldIntegration.FindUnitAcrossWorlds(bcId) is not Npc npc || !npc.CanFly)
                return;

            connection.SendPacket(new WZUnitFlyingStateChangedPacket(bcId, true));
            var sent = Interlocked.Increment(ref _flyStateOk);
            if (sent <= 5 || sent % 50 == 0)
            {
                Logger.Info(
                    "WZUnitFlyingState #{0} → zone {1} zoneId={2} bc={3} tpl={4} flying=true",
                    sent, connection.Ip, connection.ZoneId, bcId, templateId);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "WZUnitFlyingState failed bc={0} tpl={1}", bcId, templateId);
        }
    }

    public void OnRemove(ZoneConnection connection, byte[] raw)
    {
        var parsed = ZwRemoveNpcParser.TryParse(raw);
        if (parsed is null)
        {
            Logger.Warn("Rejected malformed ZWRemoveNpc zoneId={0} rawLen={1}", connection.ZoneId, raw.Length);
            return;
        }

        if (parsed.Value.RemoveDelayMilliseconds <= 0)
        {
            CompleteRemove(connection, parsed.Value.NpcId);
        }
        else
        {
            _ = CompleteRemoveAfterDelay(connection, parsed.Value);
        }

        Logger.Debug(
            "ZWRemoveNpc bc={0} removeDelayMs={1} rawLen={2} (units={3})",
            parsed.Value.NpcId, parsed.Value.RemoveDelayMilliseconds, raw.Length, connection.Units.Count);
    }

    private static async Task CompleteRemoveAfterDelay(ZoneConnection connection, ZwRemoveNpcParsed remove)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(remove.RemoveDelayMilliseconds)).ConfigureAwait(false);
        CompleteRemove(connection, remove.NpcId);
    }

    private static void CompleteRemove(ZoneConnection connection, uint bcId)
    {
        connection.Units.TryGet(bcId, out var trackedBody);
        var trackedSpawn = trackedBody == null ? null : ZwSpawnNpcParser.TryParse(trackedBody);
        var wasZoneAuthoredNpc = trackedSpawn != null;
        var hadMirror = WorldIntegration.FindUnitAcrossWorlds(bcId) is Npc { IsZoneMirror: true };

        // TryRemove is the ownership gate: duplicate remove packets and delayed callbacks after a
        // disconnect must not release an id that may already have returned to the allocator.
        if (!connection.Units.TryRemove(bcId))
            return;

        if (trackedSpawn != null)
            ZoneNpcSpawnerCatalog.SetState(connection, trackedSpawn, active: false);
        NpcStateSent.TryRemove(MarkerKey(connection.ZoneId, bcId), out _);
        WorldIntegration.OnZoneNpcRemove?.Invoke(bcId);
        if (wasZoneAuthoredNpc || hadMirror)
            ObjectIdManager.Instance.ReleaseId(bcId);
    }
}
