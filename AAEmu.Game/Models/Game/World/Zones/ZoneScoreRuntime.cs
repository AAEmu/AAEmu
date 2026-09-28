using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Owns the live zone-score table for one zone group: the score of every
/// <c>zone_score_kinds</c> row whose content points at this group, with <c>max_score</c> clamping
/// and catalog level resolution applied on every change.
/// </summary>
/// <remarks>
/// The manager is World-side state. It applies the shipped catalogs, publishes a change for each
/// accepted mutation so a sender or a zone relay can observe it, and reads and writes through an
/// injected store. It chooses no winner, starts no competition, and awards no reward.
/// </remarks>
public sealed class ZoneScoreRuntime(
    uint zoneGroupId,
    FactionScoringGameData gameData,
    IZoneScoreRuntimeStore store = null)
{
    private readonly object _lock = new();
    private readonly Dictionary<uint, ZoneScoreRuntimeEntry> _entries = [];

    /// <summary>Raised after an entry's score or level changed, and after a reset cleared it.</summary>
    public event Action<ZoneScoreApplication> ScoreChanged;

    /// <summary>Raised when a reset cleared an entry back to its level-zero baseline.</summary>
    public event Action<uint, uint> ScoreReset;

    public uint ZoneGroupId { get; } = zoneGroupId;

    /// <summary>
    /// Binds every kind whose content row names this zone group, restoring the stored score where
    /// one exists. A kind whose stored level disagrees with its stored score is corrected by the
    /// catalog rather than trusted, so a hand-edited row cannot pin an impossible level. Call once
    /// at boot, after game data is loaded.
    /// </summary>
    public void Load()
    {
        ArgumentNullException.ThrowIfNull(gameData);

        var saved = store?.Load(ZoneGroupId) ?? new Dictionary<uint, ZoneScoreRuntimeEntry>();
        lock (_lock)
        {
            _entries.Clear();
            foreach (var kind in gameData.GetZoneScoreKindsByZoneGroup(ZoneGroupId))
            {
                // zone_score_kinds.db_save gates the restore as well as the write: a kind content
                // marks transient starts from the baseline even if a row for it somehow survived.
                if (!kind.DbSave || !saved.TryGetValue(kind.Id, out var entry))
                {
                    var baseline = ZoneScoreRules.ResolveLevel(gameData, kind.Id, 0);
                    _entries[kind.Id] = new ZoneScoreRuntimeEntry(kind.Id, ZoneGroupId, 0, baseline.Level);
                    continue;
                }

                // Restore through the rules rather than copying the row: a stored score above the
                // kind's max_score is pulled back to the cap by the same path that clamps a live
                // delta, so a hand-edited row cannot resurrect a score the rules would refuse.
                var restored = ZoneScoreRules.ApplyDelta(
                    gameData, ZoneGroupId, kind.Id, 0, SaturatingDelta(entry.Score));
                _entries[kind.Id] = new ZoneScoreRuntimeEntry(
                    kind.Id, ZoneGroupId, restored.Score, restored.Level);
            }
        }
    }

    /// <summary>Returns the live entry, or a level-zero baseline when the kind has never scored.</summary>
    public ZoneScoreRuntimeEntry Get(uint kindId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var kind = gameData.GetZoneScoreKind(kindId);
        var content = gameData.GetZoneScoreContent(kind.ContentId);
        if (content.ZoneGroupId != ZoneGroupId)
            throw new InvalidOperationException(
                $"Zone score kind {kindId} belongs to zone group {content.ZoneGroupId}, not {ZoneGroupId}.");

        lock (_lock)
        {
            if (_entries.TryGetValue(kindId, out var entry))
                return entry;
            return new ZoneScoreRuntimeEntry(kindId, ZoneGroupId, 0, ZoneScoreRules.ResolveLevel(gameData, kindId, 0).Level);
        }
    }

    /// <summary>
    /// Applies a signed delta under the kind's <c>max_score</c> cap and publishes the change. A
    /// delta the cap refuses is still published, carrying the applied delta that was actually
    /// credited, so a caller can distinguish "did not score" from "scored and hit the cap".
    /// </summary>
    public ZoneScoreApplication Apply(uint kindId, int scoreDelta)
    {
        ArgumentNullException.ThrowIfNull(gameData);

        ZoneScoreApplication application;
        ZoneScoreRuntimeEntry previous;
        lock (_lock)
        {
            previous = GetLocked(gameData, kindId, _entries);
            application = ZoneScoreRules.ApplyDelta(gameData, ZoneGroupId, kindId, previous.Score, scoreDelta);
            _entries[kindId] = new ZoneScoreRuntimeEntry(
                kindId, ZoneGroupId, application.Score, application.Level);
        }

        Persist(kindId);
        ScoreChanged?.Invoke(application);
        return application;
    }

    /// <summary>
    /// Clears the entry when the kind's row opts into <paramref name="cause"/>, and reports whether
    /// it did. A kind that does not opt in keeps its score and level, and nothing is published.
    /// </summary>
    public bool Reset(uint kindId, ZoneScoreResetCause cause)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        if (!ZoneScoreRules.ResetsOn(gameData, kindId, cause))
            return false;

        ZoneScoreApplication application;
        lock (_lock)
        {
            var previous = GetLocked(gameData, kindId, _entries);
            application = ZoneScoreRules.ApplyDelta(
                gameData, ZoneGroupId, kindId, previous.Score, SaturatingDelta(-previous.Score));
            _entries[kindId] = new ZoneScoreRuntimeEntry(
                kindId, ZoneGroupId, application.Score, application.Level);
        }

        Persist(kindId);
        ScoreReset?.Invoke(kindId, ZoneGroupId);
        ScoreChanged?.Invoke(application);
        return true;
    }

    /// <summary>Every live entry, ordered by kind id for a stable list.</summary>
    public IReadOnlyList<ZoneScoreRuntimeEntry> Snapshot()
    {
        lock (_lock)
            return _entries.Values.OrderBy(entry => entry.KindId).ToArray();
    }

    private static ZoneScoreRuntimeEntry GetLocked(
        FactionScoringGameData gameData,
        uint kindId,
        Dictionary<uint, ZoneScoreRuntimeEntry> entries)
    {
        if (entries.TryGetValue(kindId, out var entry))
            return entry;
        return new ZoneScoreRuntimeEntry(kindId, 0, 0, ZoneScoreRules.ResolveLevel(gameData, kindId, 0).Level);
    }

    /// <summary>
    /// Converts a stored score into a delta the rules can apply. A score outside the rule's own
    /// <c>int</c> delta range cannot be expressed as one delta, so such a row is refused instead of
    /// being silently truncated to the cap.
    /// </summary>
    private static int SaturatingDelta(long score)
    {
        if (score > int.MaxValue)
            throw new InvalidOperationException(
                $"Stored zone score {score} exceeds the representable delta range; refusing to restore it.");
        if (score < int.MinValue)
            throw new InvalidOperationException(
                $"Stored zone score {score} is below the representable delta range; refusing to restore it.");
        return (int)score;
    }

    private void Persist(uint kindId)
    {
        if (store == null)
            return;
        // zone_score_kinds.db_save decides what survives a restart. A kind content marks transient
        // keeps its score in memory for the session and is never written, and is never restored.
        if (!gameData.GetZoneScoreKind(kindId).DbSave)
            return;
        lock (_lock)
        {
            if (_entries.TryGetValue(kindId, out var entry))
                store.Save(entry);
        }
    }
}
