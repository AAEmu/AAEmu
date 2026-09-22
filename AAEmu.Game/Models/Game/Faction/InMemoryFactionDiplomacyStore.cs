namespace AAEmu.Game.Models.Game.Faction;

/// <summary>Process-lifetime store used by tests, and by a World that has not loaded MySQL yet.</summary>
public sealed class InMemoryFactionDiplomacyStore : IFactionDiplomacyStore
{
    private readonly Dictionary<(uint, uint), FactionDiplomacyAgreement> _agreements = [];
    private readonly List<FactionDiplomacyAgreement> _history = [];
    private readonly Dictionary<(uint, uint), FactionDiplomacyCount> _counts = [];

    public IReadOnlyList<FactionDiplomacyAgreement> LoadAgreements() => _agreements.Values.Select(a => a.Clone()).ToList();

    public bool UpsertAgreement(FactionDiplomacyAgreement agreement)
    {
        if (agreement == null || agreement.Faction1 == 0 || agreement.Faction2 == 0)
            return false;
        _agreements[FactionDiplomacyRules.NormalizePair(agreement.Faction1, agreement.Faction2)] = agreement.Clone();
        return true;
    }

    public bool DeleteAgreement(uint faction1, uint faction2) =>
        _agreements.Remove(FactionDiplomacyRules.NormalizePair(faction1, faction2));

    public IReadOnlyList<FactionDiplomacyAgreement> LoadHistory(int limit)
    {
        if (limit <= 0)
            return [];
        var start = Math.Max(0, _history.Count - limit);
        return _history.Skip(start).Select(h => h.Clone()).ToList();
    }

    public bool InsertHistory(FactionDiplomacyAgreement entry)
    {
        if (entry == null || entry.Faction1 == 0 || entry.Faction2 == 0)
            return false;
        _history.Add(entry.Clone());
        return true;
    }

    public IReadOnlyList<FactionDiplomacyCount> LoadCounts() => _counts.Values.ToList();

    public bool UpsertCount(FactionDiplomacyCount count)
    {
        if (count.CharacterId == 0)
            return false;
        _counts[(count.CharacterId, count.OtherId)] = count;
        return true;
    }
}
