namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The statements a zone group's siege raid team is read with.
/// </summary>
/// <remarks>
/// Named rather than written at the call site so a test can run the same text against a database of its own:
/// a registration is the manager's only view of who is on a team, and there is nothing above the query that
/// could tell a member who is gone from one who is not.
///
/// Every one of them joins <c>characters</c> and asks for live rows (<c>c.deleted = 0</c>). Deleting a character
/// marks the row <c>deleted=1</c> and renames it, and the asset cleanup that follows does not touch
/// <c>siege_raid_team_members</c> - the registration outlives the character - so without the filter a deleted
/// character keeps a place in the team's size, stays in its member list, and is still listed in the
/// registration popup under its deleted name.
/// </remarks>
internal static class SiegeRaidTeamQueries
{
    /// <summary>
    /// The member list: every registered character of the zone group's siege, with the columns the window shows
    /// for each of them.
    /// </summary>
    internal const string MembersSql = """
        SELECT c.id, c.name, c.level, c.ability1, c.ability2, c.ability3, c.heir_exp, c.faction_id
        FROM siege_raid_team_members m
        JOIN characters c ON c.id = m.character_id
        WHERE m.zone_id = @z AND c.deleted = 0
        ORDER BY m.registered_at, m.character_id
        """;

    /// <summary>
    /// The same registrations reduced to what the teams are grouped from: who registered, and the faction they
    /// fight for.
    /// </summary>
    internal const string RosterSql = """
        SELECT m.character_id, c.faction_id
        FROM siege_raid_team_members m
        JOIN characters c ON c.id = m.character_id
        WHERE m.zone_id = @z AND c.deleted = 0
        ORDER BY m.registered_at, m.character_id
        """;

    /// <summary>
    /// The offense side alone: who registered as an attacker, for the siege_offense_hq_user target relation.
    /// </summary>
    internal const string OffenseRosterSql = """
        SELECT m.character_id
        FROM siege_raid_team_members m
        JOIN characters c ON c.id = m.character_id
        WHERE m.zone_id = @z AND m.is_offense = 1 AND c.deleted = 0
        """;

    /// <summary>
    /// The registration popup's list: every registration of the zone group in the order it was made, with the
    /// name to show against it.
    /// </summary>
    /// <remarks>
    /// A LEFT JOIN, and the predicate keeps it one: a registration whose character row is gone altogether is
    /// still listed, without a name. A character who is only soft-deleted is not - the character row is right
    /// there, but the character it names is gone.
    /// </remarks>
    internal const string RegisterListSql = """
        SELECT m.character_id, c.name
        FROM siege_raid_team_members m
        LEFT JOIN characters c ON c.id = m.character_id
        WHERE m.zone_id = @z AND (c.id IS NULL OR c.deleted = 0)
        ORDER BY m.registered_at, m.character_id
        """;
}
