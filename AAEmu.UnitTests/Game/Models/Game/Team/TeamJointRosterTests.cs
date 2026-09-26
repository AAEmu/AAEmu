using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Team;

using GameModel = AAEmu.Game.Models.Game.Team.Team;

namespace AAEmu.UnitTests.Game.Models.Game.Team;

public class TeamJointRosterTests
{
    [Test]
    public async Task Roster_KeepsLeaderFirstAndAssignsStableOrders()
    {
        var roster = new TeamJointRoster(77u, 11u);
        await Assert.That(roster.TryAdd(11u, 5, 50)).IsTrue();
        await Assert.That(roster.TryAdd(22u, 5, 50)).IsTrue();

        await Assert.That(roster.GetOrder(11u)).IsEqualTo(1u);
        await Assert.That(roster.GetOrder(22u)).IsEqualTo(2u);
        await Assert.That(roster.IsLeader(11u)).IsTrue();
        await Assert.That(roster.IsLeader(22u)).IsFalse();
        await Assert.That(roster.GetOtherTeamId(11u)).IsEqualTo(22u);
        await Assert.That(roster.GetOtherTeamId(22u)).IsEqualTo(11u);
    }

    [Test]
    public async Task Roster_RejectsDuplicateAndOverCapacityTeams()
    {
        var roster = new TeamJointRoster(77u, 11u);
        await Assert.That(roster.TryAdd(11u, 6, 10)).IsTrue();
        await Assert.That(roster.TryAdd(11u, 1, 10)).IsFalse();
        await Assert.That(roster.TryAdd(22u, 5, 10)).IsFalse();
        await Assert.That(roster.TryAdd(22u, 4, 10)).IsTrue();
    }

    [Test]
    public async Task Roster_RejectsZeroTeamNonPositiveLimitAndNonLeaderFirst()
    {
        var roster = new TeamJointRoster(77u, 11u);
        await Assert.That(roster.TryAdd(0u, 1, 50)).IsFalse();
        await Assert.That(roster.TryAdd(11u, 1, 0)).IsFalse();
        // A follower may not claim the leader slot before the declared leader is in.
        await Assert.That(roster.TryAdd(22u, 1, 50)).IsFalse();
        await Assert.That(roster.TryAdd(11u, 1, 50)).IsTrue();
        await Assert.That(roster.TryAdd(22u, 1, 50)).IsTrue();
    }

    [Test]
    public async Task Roster_FollowerRemovalReindexesRemainingTeam()
    {
        var roster = new TeamJointRoster(77u, 11u);
        roster.TryAdd(11u, 1, 50);
        roster.TryAdd(22u, 1, 50);
        roster.TryAdd(33u, 1, 50);

        await Assert.That(roster.Remove(22u)).IsTrue();
        await Assert.That(roster.GetOrder(33u)).IsEqualTo(2u);
    }

    [Test]
    public async Task Roster_LeaderRemovalIsRefusedSoManagerCanDissolve()
    {
        var roster = new TeamJointRoster(77u, 11u);
        roster.TryAdd(11u, 1, 50);
        roster.TryAdd(22u, 1, 50);

        Assert.Throws<InvalidOperationException>(() => roster.Remove(11u));
    }

    [Test]
    public async Task Rules_AllowOwnerAndRaidOfficerOnly()
    {
        var raid = new GameModel { Id = 1u, OwnerId = 5u, OfficerId = 6UL, IsParty = false };
        var party = new GameModel { Id = 2u, OwnerId = 5u, IsParty = true };

        await Assert.That(TeamJointRules.CanManage(raid, 5u)).IsTrue();
        await Assert.That(TeamJointRules.CanManage(raid, 6u)).IsTrue();
        await Assert.That(TeamJointRules.CanManage(raid, 7u)).IsFalse();
        await Assert.That(TeamJointRules.CanManage(party, 5u)).IsFalse();
    }

    [Test]
    public async Task Rules_CapacityAndBreakAuthority()
    {
        await Assert.That(TeamJointRules.Fits(30, 20, 50)).IsTrue();
        await Assert.That(TeamJointRules.Fits(30, 21, 50)).IsFalse();
        await Assert.That(TeamJointRules.Fits(51, 0, 50)).IsFalse();
        await Assert.That(TeamJointRules.Fits(-1, 0, 50)).IsFalse();

        var leader = new GameModel { Id = 1u, OwnerId = 5u, IsJointLeader = true };
        var follower = new GameModel { Id = 2u, OwnerId = 8u, IsJointLeader = false };
        await Assert.That(TeamJointRules.CanBreak(leader, 5u)).IsTrue();
        await Assert.That(TeamJointRules.CanBreak(follower, 8u)).IsFalse();
    }

    [Test]
    public async Task TeamHeader_CarriesJointStateInNativeOrder()
    {
        var team = new GameModel
        {
            Id = 5u,
            OwnerId = 7u,
            IsParty = false,
            JointId = 91u,
            IsJointLeader = true,
            JointOrder = 2
        };
        var stream = team.Write(new PacketStream());
        var expectedRollForBind = team.LootingRule.RollForBindOnPickup;
        stream.Rollback();

        await Assert.That(stream.ReadUInt32()).IsEqualTo(5u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(7UL);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        for (var i = 0; i < 10; i++)
            await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        for (var i = 0; i < 50; i++)
        {
            await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
            await Assert.That(stream.ReadBoolean()).IsFalse();
        }
        for (var i = 0; i < 12; i++)
            await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        stream.ReadByte(); // looting method
        stream.ReadByte(); // minimum grade
        stream.ReadUInt64(); // loot master
        await Assert.That(stream.ReadBoolean()).IsEqualTo(expectedRollForBind);

        await Assert.That(stream.ReadUInt32()).IsEqualTo(91u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)TeamRoleType.Raid);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
