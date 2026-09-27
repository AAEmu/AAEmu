using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Team;

using GameModel = AAEmu.Game.Models.Game.Team.Team;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TeamJointManagerTests
{
    [Test]
    public async Task TryGet_WithoutServiceProvider_IsFalseAndYieldsNull()
    {
        await Assert.That(TeamJointManager.TryGet(out var manager)).IsFalse();
        await Assert.That(manager).IsNull();
    }

    [Test]
    public async Task UnknownCharacterEntryPointsAreInert()
    {
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        var manager = new TeamJointManager(world);

        await Assert.That(manager.RequestSummons(0u)).IsEmpty();
        await Assert.That(manager.ReplyToSummon(999u, true, "Nobody")).IsFalse();
        manager.RequestJointInfo(0u, 1UL, TeamJointModes.MenuChatRequest, "Nobody", 0);
        manager.RespondToJoint(999u, 1UL, true, true, false);
        manager.RespondToJointBreak(999u, true, false);

        await Assert.That(world.Sent.Count).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
    }

    [Test]
    public async Task GetSession_IsNullUntilAFederationExists()
    {
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        world.AddCharacter(1u, "Alice").AddTeam(10u, 1u, false, 1u);
        var manager = new TeamJointManager(world);

        await Assert.That(manager.GetSession(10u)).IsNull();
    }

    [Test]
    public async Task JointCapacityComesFromTheTeamRaidLimit()
    {
        await Assert.That(GameModel.RaidMemberLimit).IsEqualTo(50);
        await Assert.That(GameModel.PartyMemberLimit).IsEqualTo(5);
    }
}
