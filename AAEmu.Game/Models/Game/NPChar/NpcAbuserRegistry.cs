using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// World-side mirror of the NPC abuser lists the zone maintains for its AI units.
/// </summary>
/// <remarks>
/// The zone owns the list and reports it with three events: a single registration (one npc, one
/// abuser), a batched unregistration (one npc, up to a hundred abusers), and a clear (one npc, every
/// abuser). The World keeps the membership because the zone owns who is fighting an npc, and the
/// World's own aggro table only owns the threat score attached to it.
/// <para>
/// Entries are only valid while both units are present, so every removal path has to reach this
/// table: the unregister and clear events themselves, the npc leaving the world, and the abuser
/// leaving the world. A register whose remover never runs leaves a live-looking row pointing at a
/// unit nothing owns any more, and the next query against it resolves a target that does not exist.
/// </para>
/// </remarks>
public static class NpcAbuserRegistry
{
    private static readonly ConcurrentDictionary<uint, HashSet<uint>> AbusersByNpc = new();

    /// <summary>Records one abuser for an npc. A repeated registration is not an error.</summary>
    public static bool Register(uint npcUnitId, uint abuserUnitId)
    {
        if (npcUnitId == 0 || abuserUnitId == 0 || abuserUnitId == npcUnitId)
            return false;

        var set = AbusersByNpc.GetOrAdd(npcUnitId, _ => new HashSet<uint>());
        lock (set)
            return set.Add(abuserUnitId);
    }

    /// <summary>
    /// Drops the listed abusers from one npc. Unknown ids are ignored, and an npc left with no
    /// abusers loses its row entirely rather than keeping an empty set.
    /// </summary>
    public static int Unregister(uint npcUnitId, IReadOnlyList<uint> abuserUnitIds)
    {
        if (npcUnitId == 0 || abuserUnitIds == null || abuserUnitIds.Count == 0)
            return 0;

        if (!AbusersByNpc.TryGetValue(npcUnitId, out var set))
            return 0;

        var removed = 0;
        lock (set)
        {
            foreach (var abuserUnitId in abuserUnitIds)
            {
                if (set.Remove(abuserUnitId))
                    removed++;
            }

            // Drop the key while still holding the set lock. A concurrent Register that re-adds the
            // key between an emptiness test and the removal would otherwise be deleted along with
            // it, losing a live registration with no event left to restore it.
            if (set.Count == 0)
                AbusersByNpc.TryRemove(new KeyValuePair<uint, HashSet<uint>>(npcUnitId, set));
        }

        return removed;
    }

    /// <summary>Drops every abuser of one npc.</summary>
    public static bool Clear(uint npcUnitId)
    {
        if (npcUnitId == 0)
            return false;

        return AbusersByNpc.TryRemove(npcUnitId, out _);
    }

    /// <summary>Drops one npc's row when the npc itself leaves the world.</summary>
    public static bool ForgetNpc(uint npcUnitId) => Clear(npcUnitId);

    /// <summary>
    /// Drops one abuser from every npc that lists it, used when the abuser leaves the world. Without
    /// this a departed unit stays registered against every npc it ever fought.
    /// </summary>
    public static int ForgetUnit(uint abuserUnitId)
    {
        if (abuserUnitId == 0)
            return 0;

        var removed = 0;
        foreach (var npcUnitId in AbusersByNpc.Keys.ToArray())
        {
            if (!AbusersByNpc.TryGetValue(npcUnitId, out var set))
                continue;

            lock (set)
            {
                if (set.Remove(abuserUnitId))
                    removed++;
                if (set.Count == 0)
                    AbusersByNpc.TryRemove(new KeyValuePair<uint, HashSet<uint>>(npcUnitId, set));
            }
        }

        return removed;
    }

    /// <summary>Current abusers of one npc, empty when it has none.</summary>
    public static IReadOnlyList<uint> GetAbusers(uint npcUnitId)
    {
        if (npcUnitId == 0 || !AbusersByNpc.TryGetValue(npcUnitId, out var set))
            return [];

        lock (set)
            return set.ToArray();
    }

    /// <summary>Whether an npc currently lists an abuser.</summary>
    public static bool IsRegistered(uint npcUnitId, uint abuserUnitId)
    {
        if (npcUnitId == 0 || !AbusersByNpc.TryGetValue(npcUnitId, out var set))
            return false;

        lock (set)
            return set.Contains(abuserUnitId);
    }

    /// <summary>
    /// Whether the zone has an opinion at all about this npc. False means "not reported", not
    /// "no abusers": a caller must keep its own behaviour in that case rather than treat the npc
    /// as having nobody on it.
    /// </summary>
    public static bool HasEntry(uint npcUnitId) => npcUnitId != 0 && AbusersByNpc.ContainsKey(npcUnitId);

    /// <summary>
    /// The unit the zone says this npc is fighting, scored by the World's own aggro table.
    /// </summary>
    /// <remarks>
    /// The zone owns membership of the abuser list and the World owns the threat score, so the
    /// World's rows are filtered by the zone's list before the top is taken: a unit the zone has
    /// stopped listing is not this npc's target however much threat the World still holds for it.
    /// A tie on threat goes to the lower unit id, so the answer does not depend on the order the
    /// aggro dictionary happens to enumerate in. Null means the zone listed nobody this table can
    /// name, which is not the same as the zone listing nobody.
    /// </remarks>
    public static Unit? SelectZoneReportedTarget(Npc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);

        if (!AbusersByNpc.TryGetValue(npc.ObjId, out var set))
            return null;

        Unit? best = null;
        var bestAggro = 0;
        lock (set)
        {
            foreach (var row in npc.AggroTable)
            {
                if (!set.Contains(row.Key))
                    continue;

                var owner = row.Value.Owner;
                if (owner == null)
                    continue;

                var total = row.Value.TotalAggro;
                if (best == null || total > bestAggro || (total == bestAggro && owner.ObjId < best.ObjId))
                {
                    best = owner;
                    bestAggro = total;
                }
            }
        }

        return best;
    }

    /// <summary>Number of npcs holding at least one abuser. Test and diagnostics hook.</summary>
    public static int TrackedNpcCount => AbusersByNpc.Count;

    /// <summary>Total registrations across every npc. Test and diagnostics hook.</summary>
    public static int TotalEntryCount
    {
        get
        {
            var total = 0;
            foreach (var set in AbusersByNpc.Values)
            {
                lock (set)
                    total += set.Count;
            }

            return total;
        }
    }

    /// <summary>Drops all state. Test isolation only.</summary>
    public static void Reset() => AbusersByNpc.Clear();
}
