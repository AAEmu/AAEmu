using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// One concrete World-side action derived from a <c>conflict_zone_npc_spawners</c> row: arm or
/// retire the placement with the activate flag the row carried. Purely content-derived; the id is
/// the zone-local placement id from <c>npc_spawners.g</c>, not <c>npc_spawners.id</c>.
/// </summary>
/// <param name="NpcSpawnerId">Zone-local placement id (<c>conflict_zone_npc_spawners.npc_spawner_id</c>).</param>
/// <param name="Activate">True arms the placement, false deactivates it.</param>
/// <param name="UseDespawn">The row's <c>use_despawn</c> flag: despawn live NPCs on deactivation.</param>
public readonly record struct ConflictZoneSpawnerAction(uint NpcSpawnerId, bool Activate, bool UseDespawn);

/// <summary>
/// Maps a conflict zone's honor-point war state onto the spawner set that should be armed, using
/// only the shipped <c>conflict_zone_npc_spawners</c> rows for the group.
/// </summary>
/// <remarks>
/// The Zone host keeps the war state for unit-requirement checks but never arms the spawners from
/// it, so World owns this toggle. The state → spawner-kind mapping is exactly the shipped
/// <c>enum_conflict_zone_state_kinds</c> contract:
/// <list type="bullet">
///   <item><description><see cref="ZoneConflictType.War"/> (war) → <see cref="ConflictZoneStateKind.War"/>.</description></item>
///   <item><description><see cref="ZoneConflictType.Peace"/> (peace) → <see cref="ConflictZoneStateKind.Peace"/>.</description></item>
///   <item><description>every escalation step (tension…conflict) and battle → no dedicated spawner set.</description></item>
/// </list>
/// Escalation states carry no spawner rows in the shipped content, so they resolve to an empty
/// action set rather than guessing a fallback. A missing group or a group with no rows also yields
/// an empty set. No id, coordinate, radius, or rate is invented here — the caller owns the
/// per-placement geometry and the packet.
/// </remarks>
public static class ConflictZoneSpawnerRules
{
    /// <summary>
    /// The spawner state kind a war state arms. Returns <see cref="ConflictZoneStateKind.None"/> for
    /// escalation states that have no dedicated spawner rows.
    /// </summary>
    public static ConflictZoneStateKind ResolveStateKind(ZoneConflictType state) => state switch
    {
        ZoneConflictType.War => ConflictZoneStateKind.War,
        ZoneConflictType.Peace => ConflictZoneStateKind.Peace,
        _ => ConflictZoneStateKind.None
    };

    /// <summary>
    /// The concrete per-placement actions for the rows whose <c>zone_state_kind_id</c> matches the
    /// state's kind, in stable id order. Only the state's own rows are returned; use
    /// <see cref="BuildPlan"/> to also get the complementary (other-state) retirement set.
    /// </summary>
    public static IReadOnlyList<ConflictZoneSpawnerAction> ResolveActions(
        ushort zoneGroupId,
        ZoneConflictType state,
        IReadOnlyList<ConflictZoneSpawnerEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var kind = ResolveStateKind(state);
        if (kind == ConflictZoneStateKind.None || rows.Count == 0)
            return [];

        var actions = new List<ConflictZoneSpawnerAction>(rows.Count);
        foreach (var row in rows)
        {
            if (row.ZoneStateKindId != kind)
                continue;
            actions.Add(new ConflictZoneSpawnerAction(row.NpcSpawnerId, row.SpawnActivate, row.UseDespawn));
        }

        // Stable order keeps a republish (ZoneLoaded / reconnect) deterministic and diffable.
        actions.Sort(static (a, b) => a.NpcSpawnerId.CompareTo(b.NpcSpawnerId));
        return actions;
    }

    /// <summary>
    /// Convenience overload that pulls the group's rows from the loaded game data. Production callers
    /// use this; the row-list overload above is the seam the unit tests drive.
    /// </summary>
    public static IReadOnlyList<ConflictZoneSpawnerAction> ResolveActions(
        ushort zoneGroupId,
        ZoneConflictType state,
        ConflictZoneGameData gameData)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        return ResolveActions(zoneGroupId, state, gameData.GetSpawners(zoneGroupId));
    }

    /// <summary>
    /// The complete toggle for one group in one state, as the World relay applies it to each owning
    /// zone: the rows of the active state (armed or retired per their own <c>spawn_activate</c>) plus
    /// the retirement of the complementary state's rows.
    /// </summary>
    /// <remarks>
    /// A peace↔war switch must retire the state being left, not only arm the state being entered:
    /// groups 63 and 147 carry both peace and war rows, so entering war arms the war rows and
    /// deactivates the peace rows (and vice versa). Rows whose <c>spawn_activate=false</c> stay an
    /// explicit deactivation within their own state (group 139's single peace row) and are re-armed
    /// when their state is left, because leaving that state means the row no longer suppresses the
    /// placement. A placement id never appears in both sets.
    /// </remarks>
    public static ConflictZoneSpawnerPlan BuildPlan(
        ushort zoneGroupId,
        ZoneConflictType state,
        IReadOnlyList<ConflictZoneSpawnerEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var kind = ResolveStateKind(state);
        if (kind == ConflictZoneStateKind.None || rows.Count == 0)
            return new ConflictZoneSpawnerPlan([], []);

        var arm = new List<ConflictZoneSpawnerAction>(rows.Count);
        var retire = new List<ConflictZoneSpawnerAction>(rows.Count);

        foreach (var row in rows)
        {
            if (row.ZoneStateKindId == kind)
            {
                // In-state: honour the row's own flag verbatim.
                var action = new ConflictZoneSpawnerAction(row.NpcSpawnerId, row.SpawnActivate, row.UseDespawn);
                (action.Activate ? arm : retire).Add(action);
            }
            else if (row.ZoneStateKindId == ComplementaryKind(kind))
            {
                // Complementary state being left: the row no longer applies, so the placement reverts
                // to the opposite of what that row enforced — an armed row is retired, a suppressed
                // row is re-armed.
                var action = new ConflictZoneSpawnerAction(row.NpcSpawnerId, !row.SpawnActivate, row.UseDespawn);
                (action.Activate ? arm : retire).Add(action);
            }
        }

        // Stable order keeps a republish (ZoneLoaded / reconnect) deterministic and diffable.
        arm.Sort(static (a, b) => a.NpcSpawnerId.CompareTo(b.NpcSpawnerId));
        retire.Sort(static (a, b) => a.NpcSpawnerId.CompareTo(b.NpcSpawnerId));
        return new ConflictZoneSpawnerPlan(arm, retire);
    }

    /// <summary>The other peace/war spawner kind — the state whose rows are retired when this one is active.</summary>
    private static ConflictZoneStateKind ComplementaryKind(ConflictZoneStateKind kind) => kind switch
    {
        ConflictZoneStateKind.War => ConflictZoneStateKind.Peace,
        ConflictZoneStateKind.Peace => ConflictZoneStateKind.War,
        _ => ConflictZoneStateKind.None
    };
}

/// <summary>
/// The per-state toggle decision for one zone group: which placements to arm and which to retire.
/// Both lists are placement-id sets derived only from the shipped rows; the caller owns geometry and
/// the wire packets.
/// </summary>
/// <param name="Arm">
/// Placements to arm — the relay announces each with activate=true.
/// </param>
/// <param name="Retire">
/// Placements to retire — the relay announces each with activate=false and, when the row's
/// <c>use_despawn</c> is set, retires their live NPCs.
/// </param>
public readonly record struct ConflictZoneSpawnerPlan(
    IReadOnlyList<ConflictZoneSpawnerAction> Arm,
    IReadOnlyList<ConflictZoneSpawnerAction> Retire);
