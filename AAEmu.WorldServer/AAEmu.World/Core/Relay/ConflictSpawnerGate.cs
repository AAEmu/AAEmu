using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Holds the conflict-zone placements that must not hold live NPCs in the group's current war state,
/// and enforces it on the zone-authority spawn path.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>WZActivateNpcSpawnersInArea</c> (0x042) cannot address a single
/// placement: it carries only a centre and a radius. Measured against the shipped
/// <c>npc_spawners.g</c> files, the configured 1024 m arming circle covers 126–208 of a zone's
/// placements — for conflict group 15 in zone 137 it covers <b>all 208</b>. Using 0x042 to toggle
/// conflict spawners therefore switched off almost every native spawner in the zone, and because
/// nothing gated the resulting <c>ZWSpawnNpc</c> by conflict state, the re-arms from
/// <see cref="PlayerEnterService"/> and the schedule-window paths brought war NPCs back during
/// peace. The circles are no longer used for conflict state at all.
/// </para>
/// <para>
/// Enforcement rides the acknowledgement, exactly as the schedule gate does: a closed placement
/// receives <c>WZNpcSpawnFailed</c>, so Zone never runs <c>NpcManager::Create</c> and the NPC exists
/// nowhere. The set is built <b>only</b> from the group's
/// <c>conflict_zone_npc_spawners</c> rows, so a native placement that appears in no such row is
/// never a member and can never be suppressed — which is the property the circle could not provide.
/// </para>
/// <para>
/// The key is placement id <b>and</b> spawner type, because the two collide in the shipped data
/// (group 63's two peace rows, group 147's 24478/24479): matching on id alone would close the
/// wrong placement. Placement ids are the zone-local <c>npc_spawners.g</c> <c>spawnerId</c>, which
/// is the same value the Zone puts in the first field of <c>ZWSpawnNpc</c>.
/// </para>
/// </remarks>
public static class ConflictSpawnerGate
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object GateLock = new();

    /// <summary>
    /// Closed placements per conflict zone group. Swapped whole per group, never mutated in place,
    /// so the hot-path read needs no lock.
    /// </summary>
    private static volatile Dictionary<ushort, HashSet<ConflictSpawnerKey>> _closedByGroup = new();

    /// <summary>
    /// zoneId → conflict zone group. Resolving a group means a template lookup, so the answer is
    /// memoised; the conflict zone topology is fixed for the process.
    /// </summary>
    private static readonly ConcurrentDictionary<uint, ushort> ZoneGroupCache = new();

    private static long _suppressed;

    /// <summary>Closed placement count for one group (0 when the group is not a conflict group).</summary>
    public static int ClosedCount(ushort zoneGroupId) =>
        _closedByGroup.TryGetValue(zoneGroupId, out var set) ? set.Count : 0;

    /// <summary>Total closed placements across every conflict group.</summary>
    public static int ClosedTotal
    {
        get
        {
            var total = 0;
            foreach (var set in _closedByGroup.Values)
                total += set.Count;
            return total;
        }
    }

    public static long SuppressedCount => Interlocked.Read(ref _suppressed);

    /// <summary>
    /// The conflict zone group a zone key belongs to.
    /// </summary>
    /// <remarks>
    /// This deliberately reads <see cref="ConflictZoneSpawnerRelay.ResolveZoneGroup"/> rather than
    /// keeping a second copy of the lookup. The gate and the publisher must never disagree about
    /// which group a zone is in: if they did, the gate would answer for a different group than the
    /// one the closed set was published under, and a placement would be either wrongly suppressed or
    /// wrongly allowed. In production both resolve the same zone-catalog expression, so sharing the
    /// seam costs nothing and makes the divergence unrepresentable in tests too.
    /// </remarks>
    internal static Func<uint, ushort> ResolveZoneGroup { get; set; } =
        zoneId => (ushort)(ConflictZoneSpawnerRelay.ResolveZoneGroup(zoneId) & 0xFFFF);

    /// <summary>Restores the production resolver after a test overrides it.</summary>
    internal static void ResetForTest()
    {
        lock (GateLock)
        {
            _closedByGroup = new Dictionary<ushort, HashSet<ConflictSpawnerKey>>();
            ZoneGroupCache.Clear();
            _suppressed = 0;
        }

        ResolveZoneGroup = zoneId => (ushort)(ConflictZoneSpawnerRelay.ResolveZoneGroup(zoneId) & 0xFFFF);
    }

    /// <summary>
    /// True when this exact placement (id <b>and</b> type) may not hold live NPCs right now because
    /// its group's conflict state does not currently authorise it. Called once per
    /// <c>ZWSpawnNpc</c>, so the common case — no conflict group has published anything — costs one
    /// dictionary count check and returns.
    /// </summary>
    public static bool IsClosed(uint zoneId, uint spawnerId, uint spawnerType)
    {
        if (spawnerId == 0 || spawnerType == 0)
            return false;

        var byGroup = _closedByGroup;
        if (byGroup.Count == 0)
            return false;

        var groupId = GroupOfZone(zoneId);
        if (groupId == 0)
            return false;

        return byGroup.TryGetValue(groupId, out var closed)
            && closed.Contains(new ConflictSpawnerKey(spawnerId, spawnerType));
    }

    /// <summary>
    /// Replaces one group's closed set wholesale. An empty set removes the group entirely so a
    /// group whose rows no longer apply stops costing a lookup on the spawn path.
    /// </summary>
    internal static void Publish(ushort zoneGroupId, HashSet<ConflictSpawnerKey> closed)
    {
        lock (GateLock)
        {
            var next = new Dictionary<ushort, HashSet<ConflictSpawnerKey>>(_closedByGroup);
            if (closed.Count == 0)
                next.Remove(zoneGroupId);
            else
                next[zoneGroupId] = closed;

            _closedByGroup = next;
        }

        Logger.Info(
            "ConflictSpawnerGate group={0} closed={1} (total across groups {2})",
            zoneGroupId, closed.Count, ClosedTotal);
    }

    /// <summary>Records a withheld acknowledgement; logs at a decreasing rate under a flood.</summary>
    internal static void CountSuppressed(
        uint zoneId, uint groupId, uint spawnerId, uint spawnerType, uint templateId)
    {
        var n = Interlocked.Increment(ref _suppressed);
        if (n <= 5 || n % 250 == 0)
        {
            Logger.Info(
                "ConflictSpawnerGate suppressed #{0} zoneId={1} group={2} sid={3} sType={4} tpl={5} — " +
                "placement is not authorised in the group's current conflict state",
                n, zoneId, groupId, spawnerId, spawnerType, templateId);
        }
    }

    private static ushort GroupOfZone(uint zoneId)
    {
        if (ZoneGroupCache.TryGetValue(zoneId, out var cached))
            return cached;

        var groupId = ResolveZoneGroup(zoneId);
        ZoneGroupCache[zoneId] = groupId;
        return groupId;
    }
}

/// <summary>
/// One conflict placement identity: the zone-local <c>spawnerId</c> and its spawner type. Both are
/// required — spawner types collide across placements in the shipped data, so id alone would close
/// the wrong placement.
/// </summary>
public readonly record struct ConflictSpawnerKey(uint SpawnerId, uint SpawnerType);
