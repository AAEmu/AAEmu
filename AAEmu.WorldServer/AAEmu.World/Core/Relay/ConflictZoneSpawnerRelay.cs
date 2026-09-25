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
/// Drives the shipped <c>conflict_zone_npc_spawners</c> rows for a conflict zone group: when the
/// group enters war or peace, the placements bound to that state are armed and the placements bound
/// to the complementary state are retired, so the spawners visibly toggle.
/// </summary>
/// <remarks>
/// <para>
/// The Zone host receives the war state on WZConflictZoneState (0x084) and stores it, but the only
/// consumers of that state are the unit/skill-requirement evaluators — the host never arms the
/// conflict spawners itself. World therefore owns the toggle and reuses the existing
/// WZActivateNpcSpawnersInArea (0x042) path the player-scoped and prewarm arming already use.
/// </para>
/// <para>
/// 0x042 is a circle (centre + radius), not a per-placement id, so each placement is announced as a
/// circle centred on the placement's own zone-local coordinates. The radius is the existing typed
/// <c>NpcSpawnerActivate.Radius</c> knob; no radius, id, or coordinate is invented here. A placement
/// that is not present in the zone's <c>npc_spawners.g</c> catalog is skipped and logged — the
/// content id is never invented to fill a gap.
/// </para>
/// <para>
/// Retirement follows the schedule gate's deferred-despawn pattern: the bcId stays registered and
/// stays allocated until the Zone answers with its own ZWRemoveNpc, so a late confirmation cannot
/// free an id another unit already took.
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

    /// <summary>Restores the production resolvers after a test overrides them.</summary>
    internal static void ResetForTest() =>
        (ResolvePlacements, ResolveZoneGroup, ResolveRows) = (ZoneSpawnerPlacementCatalog.GetAll,
            zoneId => ZoneManager.Instance.GetZoneByKey(zoneId)?.GroupId ?? 0,
            groupId => AAEmu.Game.GameData.ConflictZoneGameData.Instance.GetSpawners(groupId));

    /// <summary>
    /// The shipped <c>conflict_zone_npc_spawners</c> rows for a group. Defaults to the loaded game
    /// data; the unit tests override it to drive the toggle without a content database.
    /// </summary>
    internal static Func<ushort, IReadOnlyList<ConflictZoneSpawnerEntry>> ResolveRows { get; set; } =
        groupId => AAEmu.Game.GameData.ConflictZoneGameData.Instance.GetSpawners(groupId);

    /// <summary>
    /// Re-arm the group on every transition and on every ZoneLoaded. <paramref name="warState"/> is
    /// the same byte the WZConflictZoneState packet carries (<see cref="ZoneConflictType"/>); it is
    /// taken as a byte so the two existing Program.cs hook points forward the wire value unchanged,
    /// and an unknown byte degrades to the empty (no dedicated spawner) set.
    /// </summary>
    public static void Apply(ushort zoneGroupId, byte warState)
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
        if (plan.Arm.Count == 0 && plan.Retire.Count == 0)
        {
            // Escalation states (tension…conflict, battle) carry no dedicated spawner rows, and a
            // group with no rows has nothing to toggle. Not an error: the war state is still on the
            // wire for requirement checks.
            Logger.Debug(
                "ConflictZoneSpawnerRelay group={0} state={1} — no spawner rows for this state",
                zoneGroupId, state);
            return;
        }

        var armed = 0;
        var deactivated = 0;
        var skipped = 0;

        foreach (var zone in PlayerEnterService.AllLoadedZones())
        {
            if (ResolveZoneGroup(zone.ZoneId) != zoneGroupId)
                continue;

            var placements = ResolvePlacements(zone.ZoneId);
            if (placements.Count == 0)
            {
                Logger.Warn(
                    "ConflictZoneSpawnerRelay group={0} zoneId={1} — no npc_spawners.g placements parsed; skipping",
                    zoneGroupId, zone.ZoneId);
                continue;
            }

            var byId = new Dictionary<uint, ZoneSpawnerPlacementCatalog.SpawnerPlacement>(placements.Count);
            foreach (var placement in placements)
                byId[placement.PlacementId] = placement;

            foreach (var action in plan.Arm)
            {
                if (TryResolve(byId, zone, zoneGroupId, action, out var armPlacement))
                {
                    SendArm(zone, zoneGroupId, armPlacement);
                    armed++;
                }
                else
                {
                    skipped++;
                }
            }

            foreach (var action in plan.Retire)
            {
                if (!TryResolve(byId, zone, zoneGroupId, action, out var retirePlacement))
                {
                    skipped++;
                    continue;
                }

                SendRetire(zone, zoneGroupId, retirePlacement, action);
                deactivated++;
            }
        }

        if (armed > 0 || deactivated > 0 || skipped > 0)
        {
            Logger.Info(
                "ConflictZoneSpawnerRelay group={0} state={1} armed={2} deactivated={3} skipped={4}",
                zoneGroupId, state, armed, deactivated, skipped);
        }
    }

    /// <summary>
    /// Resolves a placement id to its zone-local geometry. Returns false (and counts a skip) when
    /// the zone's <c>npc_spawners.g</c> does not carry the id — the content id is never invented.
    /// </summary>
    private static bool TryResolve(
        Dictionary<uint, ZoneSpawnerPlacementCatalog.SpawnerPlacement> byId,
        ZoneConnection zone,
        ushort groupId,
        ConflictZoneSpawnerAction action,
        out ZoneSpawnerPlacementCatalog.SpawnerPlacement placement)
    {
        if (byId.TryGetValue(action.NpcSpawnerId, out placement))
            return true;

        Logger.Warn(
            "ConflictZoneSpawnerRelay group={0} zoneId={1} placement={2} is not in this zone's npc_spawners.g — skipped",
            groupId, zone.ZoneId, action.NpcSpawnerId);
        return false;
    }

    /// <summary>
    /// Arms one placement by announcing its own circle. The position is already zone-local because it
    /// was parsed from <c>npc_spawners.g</c>, which is the space 0x042 is evaluated in.
    /// </summary>
    private static void SendArm(
        ZoneConnection zone,
        ushort group,
        ZoneSpawnerPlacementCatalog.SpawnerPlacement placement)
    {
        var radius = ArmRadius();
        zone.SendPacket(new WZActivateNpcSpawnersInAreaPacket(
            placement.X, placement.Y, placement.Z, radius, activate: true));
        Logger.Debug(
            "ConflictZoneSpawnerRelay arm group={0} zoneId={1} placement={2} local=({3:F1},{4:F1},{5:F1}) r={6:F0}",
            group, zone.ZoneId, placement.PlacementId, placement.X, placement.Y, placement.Z, radius);
    }

    /// <summary>
    /// Retires one placement: deactivate its circle, then, when the row's <c>use_despawn</c> is set,
    /// retire whatever it currently has live through the shared deferred despawn path.
    /// </summary>
    private static void SendRetire(
        ZoneConnection zone,
        ushort group,
        ZoneSpawnerPlacementCatalog.SpawnerPlacement placement,
        ConflictZoneSpawnerAction action)
    {
        var radius = ArmRadius();
        zone.SendPacket(new WZActivateNpcSpawnersInAreaPacket(
            placement.X, placement.Y, placement.Z, radius, activate: false));

        if (action.UseDespawn)
            RetireLiveSpawns(zone, group, placement.PlacementId, placement.SpawnerType);

        Logger.Debug(
            "ConflictZoneSpawnerRelay retire group={0} zoneId={1} placement={2} despawn={3}",
            group, zone.ZoneId, placement.PlacementId, action.UseDespawn);
    }

    /// <summary>
    /// Sends GO_TO_DESPAWN for every tracked NPC announced by this exact placement, then drops the
    /// mirror via <c>OnZoneNpcRemove</c> and forgets the NpcStateSent marker.
    /// </summary>
    /// <remarks>
    /// The bcId stays registered in <c>zone.Units</c> and stays allocated: the Zone answers
    /// WZNpcStartDespawn with its own ZWRemoveNpc, and that is what retires the id through the
    /// ordinary path. Releasing it here would let ObjectIdManager hand the same id to a new unit
    /// before that confirmation lands, and the late ZWRemoveNpc would then delete the wrong one.
    /// </remarks>
    private static void RetireLiveSpawns(
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
    }

    /// <summary>
    /// The arming radius for a single placement. Reuses the one typed knob the other 0x042 senders
    /// use. A non-positive or non-finite configured radius is a configuration error and is rejected
    /// loudly rather than silently falling back to a magic default.
    /// </summary>
    private static float ArmRadius()
    {
        var configured = WorldRuntime.Config.NpcSpawnerActivate.Radius;
        if (!float.IsFinite(configured) || configured <= 0f)
        {
            throw new InvalidOperationException(
                "NpcSpawnerActivate.Radius must be a finite value > 0 for conflict spawner arming");
        }

        return configured;
    }
}
