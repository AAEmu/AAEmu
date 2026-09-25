using AAEmu.Game.Models.Game.Faction;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Transport-only CvF/FvF relay. A publication supplies versioned full-state or delta records;
/// this service accumulates the canonical state, retains removals as tombstones, and replays both
/// to each ZoneLoaded authority.
/// </summary>
public static class CombatRelationRelay
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly object Sync = new();

    private static readonly Dictionary<RelationKey, CombatRelationEntry> CvfState = [];
    private static readonly Dictionary<RelationKey, CombatRelationEntry> FvfState = [];
    private static ulong _cvfVersion;
    private static ulong _fvfVersion;
    private static bool _cvfInitialized;
    private static bool _fvfInitialized;

    public static void PublishCvF(CombatRelationPublication publication) =>
        Publish(publication, CvfState, ref _cvfVersion, ref _cvfInitialized, isCvF: true);

    public static void PublishFvF(CombatRelationPublication publication) =>
        Publish(publication, FvfState, ref _fvfVersion, ref _fvfInitialized, isCvF: false);

    /// <summary>
    /// Clears canonical state, tombstones, and versions; a later full-state publication may restart the sequence.
    /// This lifecycle reset does not emit tombstones; publish an empty FullState first when live zones
    /// must be cleared before reset.
    /// </summary>
    public static void Reset()
    {
        lock (Sync)
        {
            CvfState.Clear();
            FvfState.Clear();
            _cvfVersion = 0;
            _fvfVersion = 0;
            _cvfInitialized = false;
            _fvfInitialized = false;
        }
    }

    public static void PublishToZone(ZoneConnection zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.State < ZoneConnectionState.ZoneLoaded)
            throw new InvalidOperationException("Combat relations can only be replayed to a ZoneLoaded authority.");

        lock (Sync)
        {
            if (_cvfInitialized)
                SendChunks(zone, Snapshot(CvfState), isCvF: true);
            if (_fvfInitialized)
                SendChunks(zone, Snapshot(FvfState), isCvF: false);
        }
    }

    private static void Publish(
        CombatRelationPublication publication,
        Dictionary<RelationKey, CombatRelationEntry> state,
        ref ulong currentVersion,
        ref bool initialized,
        bool isCvF)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ValidatePublication(publication.Entries);

        lock (Sync)
        {
            if (publication.Version <= currentVersion)
                throw new InvalidOperationException($"Combat-relation publication {publication.Version} is stale; current version is {currentVersion}.");

            var next = new Dictionary<RelationKey, CombatRelationEntry>(state);
            if (publication.Kind == CombatRelationPublicationKind.FullState)
                next.Clear();

            var removals = new List<CombatRelationEntry>();
            var seen = new HashSet<RelationKey>();
            foreach (var entry in publication.Entries)
            {
                var key = KeyOf(entry, isCvF);
                if (!seen.Add(key))
                    throw new InvalidDataException("A combat-relation publication cannot contain the same relation more than once.");

                if (entry.Code == 0)
                {
                    if (next.Remove(key, out var removed))
                        removals.Add(new CombatRelationEntry(removed.Faction1, removed.Faction2, 0, 0));
                    continue;
                }

                // A zone insert ignores a key that is already present, so a live value
                // has to be cleared before the replacement record in the same send.
                if (state.TryGetValue(key, out var previous) && previous.Code != 0 && previous.Code != entry.Code)
                    removals.Add(new CombatRelationEntry(previous.Faction1, previous.Faction2, 0, 0));

                next[key] = entry;
            }

            if (publication.Kind == CombatRelationPublicationKind.FullState)
            {
                foreach (var existing in state)
                {
                    if (!next.ContainsKey(existing.Key))
                        removals.Add(new CombatRelationEntry(existing.Value.Faction1, existing.Value.Faction2, 0, 0));
                }
            }

            state.Clear();
            foreach (var entry in next)
                state[entry.Key] = entry.Value;
            currentVersion = publication.Version;
            initialized = true;

            var outbound = BuildOutbound(Snapshot(next), removals);
            BroadcastChunks(outbound, isCvF);
        }
    }

    private static RelationKey KeyOf(CombatRelationEntry entry, bool isCvF) =>
        isCvF ? new RelationKey(entry.Faction1, 0) : new RelationKey(entry.Faction1, entry.Faction2);

    private static CombatRelationEntry[] BuildOutbound(
        IReadOnlyList<CombatRelationEntry> snapshot,
        IReadOnlyList<CombatRelationEntry> tombstones)
    {
        var orderedRemovals = tombstones
            .GroupBy(entry => new RelationKey(entry.Faction1, entry.Faction2))
            .Select(group => group.First())
            .OrderBy(entry => entry.Faction1)
            .ThenBy(entry => entry.Faction2)
            .ToArray();
        return [.. orderedRemovals, .. snapshot];
    }

    private static CombatRelationEntry[] Snapshot(Dictionary<RelationKey, CombatRelationEntry> state) =>
        state.Values
            .OrderBy(entry => entry.Faction1)
            .ThenBy(entry => entry.Faction2)
            .ToArray();

    private static void ValidatePublication(IReadOnlyList<CombatRelationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
    }

    private static void BroadcastChunks(IReadOnlyList<CombatRelationEntry> entries, bool isCvF)
    {
        foreach (var zone in PlayerEnterService.AllLoadedZones())
            SendChunks(zone, entries, isCvF);
    }

    private static void SendChunks(ZoneConnection zone, IReadOnlyList<CombatRelationEntry> entries, bool isCvF)
    {
        var name = isCvF ? "WZCvFCombatRelationship" : "WZFvFCombatRelationship";
        if (entries.Count == 0)
        {
            ZonePacket empty = isCvF
                ? new WZCvFCombatRelationshipPacket([])
                : new WZFvFCombatRelationshipPacket([]);
            TrySend(zone, empty, name, 0);
            return;
        }

        for (var offset = 0; offset < entries.Count; offset += WZCombatRelationPacket.MaxEntriesPerPacket)
        {
            var count = Math.Min(WZCombatRelationPacket.MaxEntriesPerPacket, entries.Count - offset);
            var chunk = new CombatRelationEntry[count];
            for (var i = 0; i < count; i++)
                chunk[i] = entries[offset + i];

            ZonePacket packet = isCvF
                ? new WZCvFCombatRelationshipPacket(chunk)
                : new WZFvFCombatRelationshipPacket(chunk);
            TrySend(zone, packet, name, count);
        }
    }

    private static void TrySend(ZoneConnection zone, ZonePacket packet, string name, int? count = null)
    {
        try
        {
            zone.SendPacket(packet);
            Logger.Debug("{0} → zone {1} records={2}", name, zone.ZoneId, count ?? -1);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "{0} failed for zone {1}; continuing with other authorities", name, zone.ZoneId);
        }
    }

    private readonly record struct RelationKey(uint Faction1, uint Faction2);
}
