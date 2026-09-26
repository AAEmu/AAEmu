namespace AAEmu.Game.Models.Game.Sieges;

/// <summary>One alliance's siege role, from <c>siege_factions</c> and its <c>siege_faction_troops</c> rows.</summary>
/// <param name="FactionId">The alliance faction id.</param>
/// <param name="MemberCount"><c>siege_factions.member_count</c> - the alliance's fielded troop count.</param>
/// <param name="CanBeDefense">It has a defense troop, so it can hold a dominion.</param>
/// <param name="CanBeOffense">It has an offense troop, so it can attack one.</param>
public sealed record SiegeFactionRole(uint FactionId, uint MemberCount, bool CanBeDefense, bool CanBeOffense);

/// <summary>
/// The alliances a siege is fought between, and which of them plays which side.
/// </summary>
/// <remarks>
/// Read from the shipped <c>siege_factions</c> / <c>siege_faction_troops</c> pair rather than named in code:
/// the troop table already says which alliance can defend, which can attack, and which can only raid.
/// <para>
/// The raider is derived, not named: it is the one alliance with an offense troop and no defense troop, which
/// in the shipped data is the only alliance that can never hold a dominion. Content that cannot single one
/// out is refused here rather than guessed at.
/// </para>
/// </remarks>
public sealed class SiegeFactionRoles
{
    private readonly Dictionary<uint, SiegeFactionRole> _byFaction;

    private SiegeFactionRoles(Dictionary<uint, SiegeFactionRole> byFaction, uint raiderFactionId)
    {
        _byFaction = byFaction;
        RaiderFactionId = raiderFactionId;
    }

    /// <summary>The alliance that can only raid - it never holds ground, so it is the outlaw side of a siege.</summary>
    public uint RaiderFactionId { get; }

    public IEnumerable<SiegeFactionRole> All => _byFaction.Values;

    /// <summary>
    /// Folds the loaded rows into the role set, one row per alliance.
    /// </summary>
    /// <remarks>
    /// Rows for an alliance that has already been seen are merged rather than refused: the loader emits the
    /// <c>siege_factions</c> row first and then one row per <c>siege_faction_troops</c> entry, so an alliance
    /// that fields both a defense and an offense troop arrives more than once and has to become one role with
    /// both flags set. What is refused is content the settlement could not act on: a troop row for an alliance
    /// the roster does not list, an alliance id of 0, a zero troop count, an empty roster, or content that
    /// does not single out exactly one raider or leaves no alliance able to hold ground.
    /// </remarks>
    public static SiegeFactionRoles FromRows(IEnumerable<SiegeFactionRole> rows)
    {
        var byFaction = new Dictionary<uint, SiegeFactionRole>();
        foreach (var row in rows)
        {
            if (row.FactionId == 0)
                throw new InvalidOperationException("siege_factions row with faction id 0 cannot name an alliance.");
            if (row.MemberCount == 0)
                throw new InvalidOperationException(
                    $"siege_factions row for faction {row.FactionId} has member_count 0; it cannot field a troop.");
            if (byFaction.TryGetValue(row.FactionId, out var existing))
            {
                byFaction[row.FactionId] = existing with
                {
                    CanBeDefense = existing.CanBeDefense || row.CanBeDefense,
                    CanBeOffense = existing.CanBeOffense || row.CanBeOffense,
                };
                continue;
            }

            byFaction[row.FactionId] = row;
        }

        if (byFaction.Count == 0)
            throw new InvalidOperationException("No siege_factions rows: a siege has no alliances to fight between.");

        var raiders = byFaction.Values
            .Where(role => role.CanBeOffense && !role.CanBeDefense)
            .Select(role => role.FactionId)
            .ToList();
        if (raiders.Count != 1)
            throw new InvalidOperationException(
                $"siege_faction_troops names {raiders.Count} alliances that can attack but never defend " +
                $"(expected exactly one raider, found {string.Join(", ", raiders)}).");

        if (!byFaction.Values.Any(role => role.CanBeDefense))
            throw new InvalidOperationException("No siege_faction_troops row can defend; nothing could ever hold a dominion.");

        return new SiegeFactionRoles(byFaction, raiders[0]);
    }

    public bool IsRaider(uint factionId) => factionId == RaiderFactionId;

    public bool CanDefend(uint factionId) =>
        _byFaction.TryGetValue(factionId, out var role) && role.CanBeDefense;

    /// <summary>The defending alliance, refused when it is not one that can defend.</summary>
    public uint RequireDefender(uint factionId)
    {
        if (!CanDefend(factionId))
            throw new InvalidOperationException(
                $"Faction {factionId} cannot defend a dominion, so it cannot be the defender of a siege.");
        return factionId;
    }

    /// <summary>
    /// The alliance that attacks <paramref name="defenderFactionId"/>: the one alliance that is neither the
    /// defender nor the raider. Content that leaves that ambiguous or empty is refused rather than picked.
    /// </summary>
    public uint OffenseAgainst(uint defenderFactionId)
    {
        RequireDefender(defenderFactionId);

        var candidates = _byFaction.Values
            .Where(role => role.CanBeOffense && role.FactionId != defenderFactionId && role.FactionId != RaiderFactionId)
            .Select(role => role.FactionId)
            .ToList();
        if (candidates.Count != 1)
            throw new InvalidOperationException(
                $"Against defender {defenderFactionId} there are {candidates.Count} attacking alliances " +
                $"(expected exactly one, found {string.Join(", ", candidates)}).");

        return candidates[0];
    }
}
