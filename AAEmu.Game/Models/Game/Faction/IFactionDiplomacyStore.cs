namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// Hero diplomacy rows: live agreements, their history and the per-character counters. Writes
/// happen at accept / deny / expiry, not on the save tick, so a World kill loses nothing.
/// </summary>
public interface IFactionDiplomacyStore
{
    IReadOnlyList<FactionDiplomacyAgreement> LoadAgreements();
    bool UpsertAgreement(FactionDiplomacyAgreement agreement);
    bool DeleteAgreement(uint faction1, uint faction2);

    /// <summary>The newest <paramref name="limit"/> history rows, oldest first.</summary>
    IReadOnlyList<FactionDiplomacyAgreement> LoadHistory(int limit);
    bool InsertHistory(FactionDiplomacyAgreement entry);

    IReadOnlyList<FactionDiplomacyCount> LoadCounts();
    bool UpsertCount(FactionDiplomacyCount count);
}
