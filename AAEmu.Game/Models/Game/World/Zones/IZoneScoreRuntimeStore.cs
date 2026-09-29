using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Durable storage for zone-score entries, keyed by zone group and kind id. Which kinds are
/// eligible is decided by the caller's <c>zone_score_kinds.db_save</c> column, not here: the store
/// only round-trips the rows it is handed.
/// </summary>
public interface IZoneScoreRuntimeStore
{
    /// <summary>Returns the stored entries for a zone group, keyed by kind id.</summary>
    IReadOnlyDictionary<uint, ZoneScoreRuntimeEntry> Load(uint zoneGroupId);

    /// <summary>Writes or replaces one entry.</summary>
    void Save(ZoneScoreRuntimeEntry entry);
}
