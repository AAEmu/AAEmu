namespace AAEmu.Game.Models.Game.Sieges;

/// <summary>One character's place on a siege's raid-team roster, as <c>siege_raid_team_members</c> keeps it.</summary>
/// <param name="CharacterId">The registered character.</param>
/// <param name="FactionId">
/// The top-level alliance the character fights for (Nuia, Haranya or the pirates) — the faction a team is
/// named after in the window.
/// </param>
public readonly record struct SiegeRaidTeamMember(uint CharacterId, uint FactionId);

/// <summary>
/// One raid team as the siege window lists it: the faction it fights for, the place it holds in the list,
/// who leads it and how many are registered.
/// </summary>
/// <remarks>
/// The window shows a siege's teams one per faction — its frame reads <c>defense</c> to choose the defence or
/// an offence slot and shows the faction's name, so the faction is what a row is about. This World keeps a
/// roster of registered characters rather than the teams retail forms, so a team is one faction's roster.
/// </remarks>
public readonly record struct SiegeRaidTeam(
    uint FactionId,
    uint Team,
    ulong OwnerId,
    string OwnerName,
    bool Defense,
    bool IsWaitWar,
    int MemberCount);

/// <summary>
/// How a siege roster becomes the teams the window lists: one per faction, the defence first, the rest in
/// the order their factions first appear, numbered from one.
/// </summary>
public static class SiegeRaidTeamRules
{
    /// <summary>
    /// Groups a roster into one team per faction. <paramref name="defenderFactionId"/> is the faction that
    /// holds the ground being fought over, which is the team the window shows in its defence slot; zero when
    /// the zone group has no claim on it, in which case nothing is defending and no row claims to.
    /// <paramref name="isWaitWar"/> is whether the war has been declared but not started yet.
    /// </summary>
    public static List<SiegeRaidTeam> Group(IReadOnlyList<SiegeRaidTeamMember> roster, uint defenderFactionId,
        bool isWaitWar)
    {
        var teams = new List<SiegeRaidTeam>();
        if (roster is not { Count: > 0 })
            return teams;

        var hasDefender = defenderFactionId != 0;
        foreach (var group in roster
                     .GroupBy(member => member.FactionId)
                     .OrderByDescending(group => hasDefender && group.Key == defenderFactionId))
        {
            teams.Add(new SiegeRaidTeam(
                group.Key,
                (uint)teams.Count + 1,
                // Electing a raid commander is a flow this World does not run yet (the vote result packet has
                // no sender), so a team goes out without a leader rather than with a guessed one. The window
                // renders an empty emblem for a team it was told to clear the same way.
                OwnerId: 0,
                OwnerName: string.Empty,
                Defense: hasDefender && group.Key == defenderFactionId,
                IsWaitWar: isWaitWar,
                MemberCount: group.Count()));
        }

        return teams;
    }
}
