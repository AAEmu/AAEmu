namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// Durable storage for faction-competition scores, keyed by competition and faction. There is no
/// shipped column saying which scores survive a restart, so every awarded score is written and the
/// competition's own reset is the only thing that clears a row.
/// </summary>
public interface IFactionCompetitionRuntimeStore
{
    /// <summary>Returns every stored score, keyed by (competition id, faction id).</summary>
    IReadOnlyDictionary<(uint CompetitionId, uint FactionId), long> LoadAll();

    /// <summary>Writes or replaces one faction's score in one competition.</summary>
    void Save(uint competitionId, uint factionId, long score);

    /// <summary>Drops one faction's row, as a competition reset does.</summary>
    void Delete(uint competitionId, uint factionId);
}
