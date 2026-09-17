using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.UnitTests.Game.Models.Game.Sieges;

/// <summary>
/// How a registration roster becomes the teams the siege window lists. A team is one faction's roster; which
/// of them is the defence is the faction holding the ground, not a fact the roster carries.
/// </summary>
public class SiegeRaidTeamRulesTests
{
    private const uint Nuia = 148;
    private const uint Haranya = 149;
    private const uint Outlaw = 114;

    private static SiegeRaidTeamMember Member(uint characterId, uint factionId) => new(characterId, factionId);

    [Test]
    public async Task Group_PutsTheDefendingFactionFirstAndNumbersFromOne()
    {
        SiegeRaidTeamMember[] roster =
        [
            Member(11, Haranya),
            Member(12, Nuia),
            Member(13, Haranya)
        ];

        var teams = SiegeRaidTeamRules.Group(roster, Nuia, isWaitWar: false);

        await Assert.That(teams.Count).IsEqualTo(2);
        await Assert.That(teams[0].FactionId).IsEqualTo(Nuia);
        await Assert.That(teams[0].Team).IsEqualTo(1u);
        await Assert.That(teams[0].Defense).IsTrue();
        await Assert.That(teams[0].MemberCount).IsEqualTo(1);
        await Assert.That(teams[1].FactionId).IsEqualTo(Haranya);
        await Assert.That(teams[1].Team).IsEqualTo(2u);
        await Assert.That(teams[1].Defense).IsFalse();
        await Assert.That(teams[1].MemberCount).IsEqualTo(2);
    }

    [Test]
    public async Task Group_WithNothingClaimedLeavesEveryTeamOnTheOffenceSide()
    {
        // No Dominion claim: the ground is nobody's yet, so no team may claim to be defending it.
        SiegeRaidTeamMember[] roster = [Member(11, Nuia), Member(12, Haranya)];

        var teams = SiegeRaidTeamRules.Group(roster, defenderFactionId: 0, isWaitWar: false);

        await Assert.That(teams.Count).IsEqualTo(2);
        await Assert.That(teams[0].Defense).IsFalse();
        await Assert.That(teams[1].Defense).IsFalse();
    }

    [Test]
    public async Task Group_KeepsAThirdFactionAfterTheFirstTwo()
    {
        SiegeRaidTeamMember[] roster = [Member(11, Outlaw), Member(12, Nuia), Member(13, Haranya)];

        var teams = SiegeRaidTeamRules.Group(roster, Nuia, isWaitWar: false);

        await Assert.That(teams.Select(team => team.FactionId)).IsEquivalentTo(new[] { Nuia, Outlaw, Haranya });
        await Assert.That(teams.Select(team => team.Team)).IsEquivalentTo(new[] { 1u, 2u, 3u });
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Group_CarriesTheWaitWarStateOntoEveryTeam(bool isWaitWar)
    {
        SiegeRaidTeamMember[] roster = [Member(11, Nuia), Member(12, Haranya)];

        var teams = SiegeRaidTeamRules.Group(roster, Nuia, isWaitWar);

        await Assert.That(teams.All(team => team.IsWaitWar == isWaitWar)).IsTrue();
    }

    [Test]
    public async Task Group_SendsNoLeaderBeforeOneIsElected()
    {
        SiegeRaidTeamMember[] roster = [Member(11, Nuia)];

        var teams = SiegeRaidTeamRules.Group(roster, Nuia, isWaitWar: false);

        await Assert.That(teams[0].OwnerId).IsEqualTo(0ul);
        await Assert.That(teams[0].OwnerName).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Group_OfAnEmptyRosterIsNoTeamsAtAll()
    {
        await Assert.That(SiegeRaidTeamRules.Group([], Nuia, isWaitWar: false).Count).IsEqualTo(0);
        await Assert.That(SiegeRaidTeamRules.Group(null, Nuia, isWaitWar: false).Count).IsEqualTo(0);
    }
}
