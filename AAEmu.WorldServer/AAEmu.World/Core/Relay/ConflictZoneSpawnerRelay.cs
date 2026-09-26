using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Publishes the closed placement set for a conflict zone group when that group enters war or
/// peace, and despawns whatever the state being left had standing.
/// </summary>
/// <remarks>
/// <para>
/// The Zone host receives the war state on WZConflictZoneState (0x084) and stores it, but the only
/// consumers of that state are the unit/skill-requirement evaluators — the host never arms the
/// conflict spawners itself. World therefore owns the toggle.
/// </para>
/// <para>
/// <b>This relay no longer sends WZActivateNpcSpawnersInArea (0x042) for conflict state.</b> 0x042
/// carries only a centre and a radius and cannot address a single placement. Measured against the
/// shipped <c>npc_spawners.g</c> files, the configured 1024 m circle covers 126–208 of a zone's
/// placements — for conflict group 15 in zone 137 it covers all 208. Announcing a circle per
/// placement therefore switched off nearly every native spawner in the zone, and because nothing
/// gated the resulting <c>ZWSpawnNpc</c> by conflict state, the re-arms from
/// <see cref="PlayerEnterService"/> and from the schedule-window paths brought war NPCs back during
/// peace. The toggle is enforced instead by <see cref="ConflictSpawnerGate"/> on the spawn
/// announcement, which keys the exact placement and therefore never touches a native one.
/// </para>
/// <para>
/// Retirement of placements that are already live still needs an active push, and that is what
/// <c>use_despawn</c> rows get: <see cref="RetireLiveSpawns"/> matches on exact placement identity
/// (id and type) and follows the schedule gate's deferred-despawn pattern, so the bcId stays
/// registered and allocated until the Zone answers with its own ZWRemoveNpc.
/// </para>
/// </remarks>
public static class ConflictZoneSpawnerRelay
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Placements for a zone, resolved from its own <c>npc_spawners.g</c>. Defaults to the real
    /// catalog; the unit tests override it so a fake zone can be exercised without level files.
    /// </summary>
    internal static Func<uint, IReadOnlyList<ZoneSpawnerPlacementCatalog.SpawnerPlacement>> ResolvePlacements { get; set; } =
        ZoneSpawnerPlacementCatalog.GetAll;

    /// <summary>
    /// The conflict zone group a zone key belongs to (0 when it is not in a conflict zone). Defaults
    /// to the real world-template lookup; the unit tests override it.
    /// </summary>
    internal static Func<uint, uint> ResolveZoneGroup { get; set; } =
        zoneId => ZoneManager.Instance.GetZoneByKey(zoneId)?.GroupId ?? 0;

    /// <summary>
    /// Re-announces the activate sphere for a zone so a placement that just left the closed set
    /// spawns again. Defaults to the real protocol path; the unit tests substitute a recorder so the
    /// re-arm can be observed without standing up the schedule gate and its DI-owned managers.
    /// </summary>
    internal static Action<ZoneConnection, string> ReactivateSpawners { get; set; } =
        ZoneProtocolHandler.ReactivateNpcSpawners;

    /// <summary>Restores the production resolvers after a test overrides them.</summary>
    internal static void ResetForTest()
    {
        (ResolvePlacements, ResolveZoneGroup, ResolveRows) = (ZoneSpawnerPlacementCatalog.GetAll,
            zoneId => ZoneManager.Instance.GetZoneByKey(zoneId)?.GroupId ?? 0,
            groupId => AAEmu.Game.GameData.ConflictZoneGameData.Instance.GetSpawners(groupId));
        ReactivateSpawners = ZoneProtocolHandler.ReactivateNpcSpawners;
        ConflictSpawnerGate.ResetForTest();
    }

    /// <summary>
    /// The shipped <c>conflict_zone_npc_spawners</c> rows for a group. Defaults to the loaded game
    /// data; the unit tests override it to drive the toggle without a content database.
    /// </summary>
    internal static Func<ushort, IReadOnlyList<ConflictZoneSpawnerEntry>> ResolveRows { get; set; } =
        groupId => AAEmu.Game.GameData.ConflictZoneGameData.Instance.GetSpawners(groupId);

    /// <summary>
    /// The ZoneLoaded half of the toggle. A zone that has just loaded is told the group's war state
    /// and is <b>not</b> re-armed.
    /// </summary>
    /// <remarks>
    /// Re-arming exists to bring back placements that the state being left had closed, and a
    /// freshly loaded zone has never closed anything: it has no live spawns to retire and no
    /// activate sphere of its own yet, so the announce circle would be a whole-zone activate that
    /// skips the prewarm step and the deferred arming a normal load goes through. The closed set is
    /// still published, so the gate is correct for whatever the zone spawns on its own.
    /// </remarks>
    public static void ApplyOnZoneLoaded(ushort zoneGroupId, byte warState) =>
        Apply(zoneGroupId, warState, reArm: false);

    /// <summary>
    /// The transition half of the toggle. This is the only path that re-arms, because a transition
    /// is the one event that closes placements and therefore the one that leaves holes to refill.
    /// </summary>
    public static void ApplyOnTransition(ushort zoneGroupId, byte warState) =>
        Apply(zoneGroupId, warState, reArm: true);

    /// <summary>
    /// Republish the group's closed placement set and despawn whatever the state being left had
    /// standing, re-arming the zones only when <paramref name="reArm"/> is set.
    /// <paramref name="warState"/> is the same byte the WZConflictZoneState packet carries
    /// (<see cref="ZoneConflictType"/>); it is taken as a byte so the two Program.cs hook points
    /// forward the wire value unchanged, and an unknown byte degrades to the empty (no dedicated
    /// spawner) set.
    /// </summary>
    private static void Apply(ushort zoneGroupId, byte warState, bool reArm)
    {
        if (!Enum.IsDefined(typeof(ZoneConflictType), warState))
        {
            Logger.Warn(
                "ConflictZoneSpawnerRelay group={0} — unknown war state byte {1}; skipping",
                zoneGroupId, warState);
            return;
        }

        var state = (ZoneConflictType)warState;
        var plan = ConflictZoneSpawnerRules.BuildPlan(zoneGroupId, state, ResolveRows(zoneGroupId));

        // The retire set IS the closed set: exactly the placements that must not hold live NPCs while
        // this state is in force. The arm set needs no entry — those placements announce and are
        // accepted, which is the gate simply having no opinion about them.
        var closed = new HashSet<ConflictSpawnerKey>(plan.Retire.Count);
        var unresolved = new List<uint>();
        var retiredLive = 0;

        foreach (var zone in PlayerEnterService.AllLoadedZones())
        {
            if (ResolveZoneGroup(zone.ZoneId) != zoneGroupId)
                continue;

            var placements = ResolvePlacements(zone.ZoneId);
            if (placements.Count == 0)
            {
                Logger.Warn(
                    "ConflictZoneSpawnerRelay group={0} zoneId={1} — no npc_spawners.g placements parsed; " +
                    "its closed placements cannot be typed this pass",
                    zoneGroupId, zone.ZoneId);
                continue;
            }

            var byId = new Dictionary<uint, ZoneSpawnerPlacementCatalog.SpawnerPlacement>(placements.Count);
            foreach (var placement in placements)
                byId[placement.PlacementId] = placement;

            foreach (var action in plan.Retire)
            {
                if (!TryResolve(byId, action, out var placement))
                {
                    unresolved.Add(action.NpcSpawnerId);
                    continue;
                }

                closed.Add(new ConflictSpawnerKey(action.NpcSpawnerId, placement.SpawnerType));

                // Only use_despawn rows need an active push: without it an NPC that is already live
                // would keep running until it happened to die on its own.
                if (action.UseDespawn
                    && RetireLiveSpawns(zone, zoneGroupId, action.NpcSpawnerId, placement.SpawnerType))
                {
                    retiredLive++;
                }
            }
        }

        // Published before the re-arm below, and unconditionally, so a group that stops resolving its
        // placements clears a previously published closed set instead of leaving a stale one armed,
        // and so the re-arm flood is judged against this state rather than the one it replaces.
        ConflictSpawnerGate.Publish(zoneGroupId, closed);

        // A placement that has just left the closed set has no NPC standing and will not announce
        // itself again on its own: the Zone only re-announces on an activate sphere, which arrives
        // on a player enter or a schedule window. Without this the placement stays empty until one of
        // those happens by chance. Publishing first is what makes this safe — the flood passes
        // through the gate, and everything still closed is refused again rather than respawning.
        if (reArm && plan.Arm.Count > 0)
            ReArmZones(zoneGroupId, plan.Arm.Count);

        // One line per apply, not one per placement: a group whose rows do not resolve in a given
        // zone used to emit a warn for every row on every transition.
        if (unresolved.Count > 0)
        {
            Logger.Warn(
                "ConflictZoneSpawnerRelay group={0} state={1} — {2} closed placement(s) are not in their " +
                "zone's npc_spawners.g and were skipped this pass: {3}",
                zoneGroupId, state, unresolved.Count, string.Join(",", unresolved.Distinct().Order()));
        }

        if (closed.Count > 0 || unresolved.Count > 0 || retiredLive > 0)
        {
            Logger.Info(
                "ConflictZoneSpawnerRelay group={0} state={1} armed={2} closedPlacements={3} unresolved={4} retiredLive={5}",
                zoneGroupId, state, plan.Arm.Count, closed.Count, unresolved.Count, retiredLive);
        }
    }

    /// <summary>
    /// Re-announces the activate sphere for the group's loaded zones so the placements that just left
    /// the closed set spawn again. Mirrors the schedule gate's re-arm, and is a no-op for a group
    /// with nothing to re-arm.
    /// </summary>
    private static void ReArmZones(ushort zoneGroupId, int armedCount)
    {
        foreach (var zone in PlayerEnterService.AllLoadedZones())
        {
            if (ResolveZoneGroup(zone.ZoneId) != zoneGroupId)
                continue;

            ReactivateSpawners(zone, $"{armedCount} conflict placements re-armed");
        }
    }

    /// <summary>
    /// Resolves a placement id to its zone-local spawner type. Returns false when the zone's
    /// <c>npc_spawners.g</c> does not carry the id — the content id is never invented, and the caller
    /// reports the misses once per apply instead of once per row.
    /// </summary>
    private static bool TryResolve(
        Dictionary<uint, ZoneSpawnerPlacementCatalog.SpawnerPlacement> byId,
        ConflictZoneSpawnerAction action,
        out ZoneSpawnerPlacementCatalog.SpawnerPlacement placement) =>
        byId.TryGetValue(action.NpcSpawnerId, out placement);

    /// <summary>
    /// Sends GO_TO_DESPAWN for every tracked NPC announced by this exact placement, then drops the
    /// mirror via <c>OnZoneNpcRemove</c> and forgets the NpcStateSent marker. Returns the number
    /// actually retired.
    /// </summary>
    /// <remarks>
    /// The bcId stays registered in <c>zone.Units</c> and stays allocated: the Zone answers
    /// WZNpcStartDespawn with its own ZWRemoveNpc, and that is what retires the id through the
    /// ordinary path. Releasing it here would let ObjectIdManager hand the same id to a new unit
    /// before that confirmation lands, and the late ZWRemoveNpc would then delete the wrong one.
    /// </remarks>
    private static bool RetireLiveSpawns(
        ZoneConnection zone,
        ushort group,
        uint placementId,
        uint spawnerType)
    {
        var retired = 0;
        foreach (var (bcId, raw) in zone.Units.Snapshot())
        {
            var parsed = ZwSpawnNpcParser.TryParse(raw);
            if (parsed == null)
                continue;
            // Exact placement identity — id AND type. Types collide across placements in the shipped
            // data (group 63's two peace rows, group 147's 24478/24479), so matching on type alone
            // would retire the wrong NPC.
            if (parsed.SpawnerId != placementId || parsed.SpawnerType != spawnerType)
                continue;

            zone.SendPacket(new WZNpcStartDespawnPacket(bcId));
            // Idempotent: the mirror is already gone when ZWRemoveNpc repeats this.
            WorldIntegration.OnZoneNpcRemove?.Invoke(bcId);
            NpcSpawnRelay.ForgetNpcState(zone.ZoneId, zone.InstanceId, bcId);
            retired++;
        }

        if (retired > 0)
        {
            Logger.Info(
                "ConflictZoneSpawnerRelay group={0} zoneId={1} retired {2} live NPCs for placement={3} (type={4})",
                group, zone.ZoneId, retired, placementId, spawnerType);
        }

        return retired > 0;
    }
}
