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
    private static readonly Dictionary<RelationKey, CombatRelationEntry> CvfTombstones = [];
    private static readonly Dictionary<RelationKey, CombatRelationEntry> FvfTombstones = [];
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
            CvfTombstones.Clear();
            FvfTombstones.Clear();
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
            {
                var entries = BuildOutbound(Snapshot(CvfState), CvfTombstones.Values.ToArray());
                ValidatePacketSize(entries);
                TrySend(zone, new WZCvFCombatRelationshipPacket(entries), "WZCvFCombatRelationship", entries.Length);
            }
            if (_fvfInitialized)
            {
                var entries = BuildOutbound(Snapshot(FvfState), FvfTombstones.Values.ToArray());
                ValidatePacketSize(entries);
                TrySend(zone, new WZFvFCombatRelationshipPacket(entries), "WZFvFCombatRelationship", entries.Length);
            }
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
            var tombstoneState = isCvF ? CvfTombstones : FvfTombstones;
            var nextTombstones = new Dictionary<RelationKey, CombatRelationEntry>(tombstoneState);
            if (publication.Kind == CombatRelationPublicationKind.FullState)
                next.Clear();

            var tombstones = new List<CombatRelationEntry>();
            var seen = new HashSet<RelationKey>();
            foreach (var entry in publication.Entries)
            {
                var key = new RelationKey(entry.Faction1, entry.Faction2);
                if (!seen.Add(key))
                    throw new InvalidDataException("A combat-relation publication cannot contain the same faction pair more than once.");

                if (entry.RelationType == 0)
                {
                    next.Remove(key);
                    nextTombstones[key] = entry;
                    tombstones.Add(entry);
                }
                else
                {
                    next[key] = entry;
                    nextTombstones.Remove(key);
                }
            }

            if (publication.Kind == CombatRelationPublicationKind.FullState)
            {
                foreach (var key in state.Keys.Where(key => !next.ContainsKey(key)))
                {
                    var tombstone = new CombatRelationEntry(key.Faction1, key.Faction2, 0, 0);
                    nextTombstones[key] = tombstone;
                    tombstones.Add(tombstone);
                }
            }

            var snapshot = Snapshot(next);
            var outbound = BuildOutbound(snapshot, tombstones);
            ValidatePacketSize(outbound);
            ValidatePacketSize(BuildOutbound(snapshot, nextTombstones.Values.ToArray()));

            state.Clear();
            foreach (var entry in next)
                state[entry.Key] = entry.Value;
            tombstoneState.Clear();
            foreach (var entry in nextTombstones)
                tombstoneState[entry.Key] = entry.Value;
            currentVersion = publication.Version;
            initialized = true;

            ZonePacket packet = isCvF
                ? new WZCvFCombatRelationshipPacket(outbound)
                : new WZFvFCombatRelationshipPacket(outbound);
            Broadcast(packet, isCvF ? "WZCvFCombatRelationship" : "WZFvFCombatRelationship", outbound.Length);
        }
    }

    private static CombatRelationEntry[] BuildOutbound(
        IReadOnlyList<CombatRelationEntry> snapshot,
        IReadOnlyList<CombatRelationEntry> tombstones)
    {
        var orderedTombstones = tombstones
            .GroupBy(entry => new RelationKey(entry.Faction1, entry.Faction2))
            .Select(group => group.First())
            .Where(entry => !snapshot.Any(current => current.Faction1 == entry.Faction1 && current.Faction2 == entry.Faction2))
            .OrderBy(entry => entry.Faction1)
            .ThenBy(entry => entry.Faction2)
            .ToArray();
        return [.. snapshot, .. orderedTombstones];
    }

    private static CombatRelationEntry[] Snapshot(Dictionary<RelationKey, CombatRelationEntry> state) =>
        state.Values
            .OrderBy(entry => entry.Faction1)
            .ThenBy(entry => entry.Faction2)
            .ToArray();

    private static void ValidatePublication(IReadOnlyList<CombatRelationEntry> entries)
    {
        if (entries.Count > WZCombatRelationPacket.MaxEntriesPerPacket)
            throw new ArgumentOutOfRangeException(nameof(entries), $"A relation publication carries at most {WZCombatRelationPacket.MaxEntriesPerPacket} records.");
    }

    private static void ValidatePacketSize(CombatRelationEntry[] entries)
    {
        if (entries.Length > WZCombatRelationPacket.MaxEntriesPerPacket)
            throw new InvalidOperationException($"The canonical combat-relation state plus tombstones exceeds {WZCombatRelationPacket.MaxEntriesPerPacket} records.");
    }

    private static void Broadcast(ZonePacket packet, string name, int count)
    {
        foreach (var zone in PlayerEnterService.AllLoadedZones())
            TrySend(zone, packet, name, count);
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
