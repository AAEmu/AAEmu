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
/// <para>
/// The Zone host keeps the war state for unit-requirement checks but never arms the spawners from
/// it, so World owns this toggle. A row is armed for exactly one state kind — the
/// <c>enum_conflict_zone_state_kinds</c> value it carries (1 = peace, 2 = war; 0 = none, which no
/// row uses). A state therefore has a spawner opinion whenever the group has rows at all, including
/// the escalation states between them: see <see cref="BuildPlan"/> for the per-row rule and for why
/// an escalation state must not resolve to "nothing to say".
/// </para>
/// <para>
/// No id, coordinate, radius, or rate is invented here — the caller owns the per-placement geometry
/// and the packet.
/// </para>
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
    /// zone: every shipped row of the group, resolved to armed or retired for <b>this</b> state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each row states what its own state kind authorises for its placement, so the decision for any
    /// state — including the escalation states, which have no rows of their own — follows from one
    /// rule applied per row:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     a <c>spawn_activate=true</c> row is <b>closed in every state except its own</b>. It arms
    ///     the placement only while that kind is in force, so a war row is closed throughout
    ///     Tension..Conflict as well as in Peace.
    ///   </description></item>
    ///   <item><description>
    ///     a <c>spawn_activate=false</c> row is <b>closed only while its own state is in force</b> and
    ///     open everywhere else, because leaving that state means the row stops suppressing it.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Two consequences are deliberate. A peace↔war switch retires the state being left as well as
    /// arming the state being entered, which is what groups 63 and 147 (both peace and war rows)
    /// need. And an escalation state is <b>not</b> an empty plan: escalation is neither Peace nor
    /// War, so no row is in force and every <c>true</c> row's placement is closed. Treating escalation
    /// as "no opinion" would publish an empty closed set, which the gate reads as "this group has no
    /// rows" and opens everything — the war placements of the 48 <c>true</c> rows in the shipped
    /// content would come back for the whole escalation stretch, which for these groups is most of
    /// each cycle.
    /// </para>
    /// <para>
    /// A placement id never appears in both sets. A group with no rows still yields an empty plan,
    /// which is the one case where an empty result genuinely means "nothing to say".
    /// </para>
    /// </remarks>
    public static ConflictZoneSpawnerPlan BuildPlan(
        ushort zoneGroupId,
        ZoneConflictType state,
        IReadOnlyList<ConflictZoneSpawnerEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return new ConflictZoneSpawnerPlan([], []);

        var kind = ResolveStateKind(state);
        var arm = new List<ConflictZoneSpawnerAction>(rows.Count);
        var retire = new List<ConflictZoneSpawnerAction>(rows.Count);

        foreach (var row in rows)
        {
            // No row carries kind None, so during escalation every row reads as out-of-state.
            var inState = row.ZoneStateKindId == kind;
            var closed = row.SpawnActivate ? !inState : inState;

            var action = new ConflictZoneSpawnerAction(row.NpcSpawnerId, Activate: !closed, row.UseDespawn);
            (closed ? retire : arm).Add(action);
        }

        // Stable order keeps a republish (ZoneLoaded / reconnect) deterministic and diffable.
        arm.Sort(static (a, b) => a.NpcSpawnerId.CompareTo(b.NpcSpawnerId));
        retire.Sort(static (a, b) => a.NpcSpawnerId.CompareTo(b.NpcSpawnerId));
        return new ConflictZoneSpawnerPlan(arm, retire);
    }
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
