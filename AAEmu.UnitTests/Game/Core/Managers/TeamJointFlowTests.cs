using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TeamJointFlowTests
{
    private const uint TeamA = 100u;
    private const uint TeamB = 200u;
    private const uint Alice = 1u;   // owner of TeamA
    private const uint Bob = 2u;     // owner of TeamB
    private const uint Carol = 3u;   // plain member of TeamA
    private const ulong JointType = 0x5A5Au;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static (TeamJointManager Manager, FakeTeamJointContext World, Clock Time) Build()
    {
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        world.AddCharacter(Alice, "Alice").AddCharacter(Bob, "Bob").AddCharacter(Carol, "Carol");
        world.AddTeam(TeamA, Alice, false, Alice, Carol);
        world.AddTeam(TeamB, Bob, false, Bob);
        return (new TeamJointManager(world, clock), world, clock);
    }

    private static void DriveToResponsePrompt(TeamJointManager manager, bool myTeamLeader)
    {
        // Mode 3 is the only mode that opens the request frame and leaves a pending round; the two
        // menu modes are info queries and are answered without one.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        manager.RespondToJoint(Alice, JointType, myTeamLeader, true, false);
    }

    [Test]
    public async Task Request_ModeOneIsAnInfoQueryAnsweredWithTheSameModeAndNoPendingRound()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);

        var replies = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(replies.Count).IsEqualTo(1);
        await Assert.That(replies[0].Mode).IsEqualTo(TeamJointModes.MenuChatRequest);
        await Assert.That(replies[0].Info.TargetTeamId).IsEqualTo(TeamB);
        // The point of the fix: a menu query must not leave a one-minute pending request behind.
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Request_ModeTwoIsAnInfoQueryAnsweredWithTheSameMode()
    {
        var (manager, world, _) = Build();
        world.SelectedTargets[Alice] = Bob;
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuTargetRequest, string.Empty, 1);

        var replies = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(replies.Count).IsEqualTo(1);
        await Assert.That(replies[0].Mode).IsEqualTo(TeamJointModes.MenuTargetRequest);
        await Assert.That(replies[0].Info.TargetTeamId).IsEqualTo(TeamB);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
    }

    [Test]
    public async Task Request_InfoQueryForATargetThatCannotBeJoinedAnswersZeroWithoutAnError()
    {
        var (manager, world, _) = Build();
        world.AddCharacter(9u, "PartyPaul");
        world.AddTeam(300u, 9u, isParty: true, 9u);
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "PartyPaul", 1);

        var replies = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(replies.Count).IsEqualTo(1);
        await Assert.That(replies[0].Info.TargetTeamId).IsEqualTo(0u);
        await Assert.That(world.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Commit_ReachesEveryMemberWithTheStoringMode()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);

        var commits = world.PacketsTo<SCTeamJointPacket>(Alice)
            .Concat(world.PacketsTo<SCTeamJointPacket>(Carol))
            .Concat(world.PacketsTo<SCTeamJointPacket>(Bob))
            .ToArray();
        await Assert.That(commits.Length).IsEqualTo(3);
        // Mode 1 is the only value a member's client stores; anything else is relayed back and the
        // joint then exists only on the server.
        await Assert.That(commits.All(packet => packet.PacketMode == SCTeamJointPacket.PacketModeSet)).IsTrue();
        await Assert.That(commits.All(packet => packet.PacketMode == 1)).IsTrue();
        await Assert.That(commits.All(packet => packet.TargetTeamId != 0u)).IsTrue();
    }

    [Test]
    public async Task Refusal_ReachesTheRequesterWithTheStoringModeAndZeroTarget()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: false, timeout: false);

        var refusal = world.PacketsTo<SCTeamJointPacket>(Alice);
        await Assert.That(refusal.Count).IsEqualTo(1);
        await Assert.That(refusal[0].PacketMode).IsEqualTo(SCTeamJointPacket.PacketModeSetRefused);
        await Assert.That(refusal[0].TargetTeamId).IsEqualTo(0u);
    }

    [Test]
    public async Task Break_DissolveClearsTheStoredJointOnEveryMember()
    {
        // A member's client STORES the joint it is told about and keeps it until it is told of a
        // joint with no other team. The break packet alone left every member holding a joint that no
        // longer existed, so the joint menus stayed hidden and a later joint with a different raid
        // was dropped as belonging to the wrong team.
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        // Every member, on both sides, was given a joint to store.
        await Assert.That(world.CountPackets<SCTeamJointPacket>()).IsEqualTo(3);
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        // count the dissolve fan-out on its own
        world.Sent.Clear();
        world.HeadersSent.Clear();

        manager.RespondToJointBreak(Bob, ask: false, accept: true);

        // Every online member of both raids is told the joint is gone, in the storing mode with a
        // zero target team id — not just the two owners who ran the handshake.
        var clears = world.PacketsTo<SCTeamJointPacket>(Alice)
            .Concat(world.PacketsTo<SCTeamJointPacket>(Carol))
            .Concat(world.PacketsTo<SCTeamJointPacket>(Bob))
            .ToArray();
        await Assert.That(clears.Length).IsEqualTo(3);
        await Assert.That(clears.All(packet => packet.PacketMode == SCTeamJointPacket.PacketModeSet)).IsTrue();
        await Assert.That(clears.All(packet => packet.TargetTeamId == 0u)).IsTrue();
        // The break notification is still sent alongside it.
        await Assert.That(world.CountPackets<SCTeamJointBreakPacket>()).IsEqualTo(3);
    }

    [Test]
    public async Task Disband_DissolveClearsTheStoredJointOnTheSurvivingTeam()
    {
        // The same stale joint survived a disband and a last owner logging out, because neither path
        // sent anything a client drops a joint for.
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        world.Sent.Clear();

        manager.OnTeamDisbanded(TeamB);

        // Only the surviving team's online members are reachable; the disbanding team is skipped.
        var clears = world.PacketsTo<SCTeamJointPacket>(Alice)
            .Concat(world.PacketsTo<SCTeamJointPacket>(Carol))
            .ToArray();
        await Assert.That(clears.Length).IsEqualTo(2);
        await Assert.That(clears.All(packet => packet.PacketMode == SCTeamJointPacket.PacketModeSet)).IsTrue();
        await Assert.That(clears.All(packet => packet.TargetTeamId == 0u)).IsTrue();
    }

    [Test]
    public async Task Disconnect_LastOwnerLoggingOutClearsTheStoredJointOnTheSurvivingTeam()
    {
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        world.AddCharacter(Alice, "Alice").AddCharacter(Bob, "Bob");
        world.AddTeam(TeamA, Alice, false, Alice);   // Alice is the only member
        world.AddTeam(TeamB, Bob, false, Bob);
        var manager = new TeamJointManager(world, clock);

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        manager.RespondToJoint(Alice, JointType, true, true, false);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        world.Sent.Clear();

        manager.OnCharacterLogout(Alice);

        var clears = world.PacketsTo<SCTeamJointPacket>(Bob).ToArray();
        await Assert.That(clears.Length).IsEqualTo(1);
        await Assert.That(clears[0].PacketMode).IsEqualTo(SCTeamJointPacket.PacketModeSet);
        await Assert.That(clears[0].TargetTeamId).IsEqualTo(0u);
    }

    [Test]
    public async Task Request_InfoQueryAboutAnAlreadyJointedRaidReportsNoUsableTeam()
    {
        // The menu query answered with the team id of a raid that is already federated, so the client
        // offered an invite that the request path then refused. The query has to describe the raid as
        // unjoinable, the same way it already does for a party.
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Sent.Clear();
        world.Errors.Clear();

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);

        var replies = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(replies.Count).IsEqualTo(1);
        // The query is still answered, so the menu has something to render, but with no team id.
        await Assert.That(replies[0].Mode).IsEqualTo(TeamJointModes.MenuChatRequest);
        await Assert.That(replies[0].Info.TargetTeamId).IsEqualTo(0u);
        await Assert.That(world.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Request_InfoQueryAboutAPendingRaidReportsNoUsableTeam()
    {
        // Same for a raid that is already the other side of an outstanding ask: offering the invite
        // there produced a menu entry whose use failed on a second outstanding ask.
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
        world.Sent.Clear();
        world.Errors.Clear();

        // Alice asks about Bob while her own raid is the pending source, and Bob asks about Alice
        // while he is the pending target: both sides are checked, not just the target.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        manager.RequestJointInfo(Bob, JointType, TeamJointModes.MenuTargetRequest, "Alice", 1);

        var toAlice = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        var toBob = world.PacketsTo<SCTeamJointInfoPacket>(Bob);
        await Assert.That(toAlice.Count).IsEqualTo(1);
        await Assert.That(toBob.Count).IsEqualTo(1);
        await Assert.That(toAlice[0].Info.TargetTeamId).IsEqualTo(0u);
        await Assert.That(toBob[0].Info.TargetTeamId).IsEqualTo(0u);
        await Assert.That(world.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Request_InfoQueryForAFreeRaidStillReportsItsTeam()
    {
        // The guard must not swallow a usable raid: the query is how the client fills the menu in.
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);

        var replies = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(replies.Count).IsEqualTo(1);
        await Assert.That(replies[0].Info.TargetTeamId).IsEqualTo(TeamB);
        await Assert.That(replies[0].Info.MemberCount).IsEqualTo(1);
    }

    // ---------- joint request ----------

    [Test]
    public async Task Request_AnswersRequesterWithModeThreeAndOpensPending()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);

        var prompt = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(prompt.Count).IsEqualTo(1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
        await Assert.That(manager.SessionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Request_AcceptsAllFourKnownWireModesAndThreeRequestModes()
    {
        await Assert.That(TeamJointModes.IsKnownWireMode(1)).IsTrue();
        await Assert.That(TeamJointModes.IsKnownWireMode(2)).IsTrue();
        await Assert.That(TeamJointModes.IsKnownWireMode(3)).IsTrue();
        await Assert.That(TeamJointModes.IsKnownWireMode(4)).IsTrue();
        await Assert.That(TeamJointModes.IsKnownWireMode(0)).IsFalse();
        await Assert.That(TeamJointModes.IsKnownWireMode(5)).IsFalse();
        await Assert.That(TeamJointModes.IsRequestMode(1)).IsTrue();
        await Assert.That(TeamJointModes.IsRequestMode(2)).IsTrue();
        // Mode 3 is the raid popup's own "invite raid joint" entry, so it is a mode a client may
        // originate a request with too.
        await Assert.That(TeamJointModes.IsRequestMode(3)).IsTrue();
        await Assert.That(TeamJointModes.IsRequestMode(4)).IsFalse();
    }

    [Test]
    public async Task Request_RejectsResponseAndOutOfRangeModes()
    {
        var (manager, world, _) = Build();
        // Mode 4 is the response frame, not something a client may originate; 9 is out of range.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ResponsePrompt, "Bob", 1);
        manager.RequestJointInfo(Alice, JointType, 9, "Bob", 1);

        await Assert.That(world.CountPackets<SCTeamJointInfoPacket>()).IsEqualTo(0);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Request_ResolvesTargetMenuFromTheRequestersSelection()
    {
        var (manager, world, _) = Build();

        // The target-menu mode with no name and nothing selected is refused: there is nothing to
        // resolve against.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuTargetRequest, string.Empty, 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamInviteeOffline);

        // Mode 1 carries the name, so a blank one is refused the same way.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "  ", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Last().Error).IsEqualTo(ErrorMessageType.TeamInviteeOffline);

        // The target-menu mode with no name now resolves through the requester's current selection
        // and, being an info query, is answered rather than turned into a pending request.
        world.SelectedTargets[Alice] = Bob;
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuTargetRequest, string.Empty, 1);

        var reply = world.PacketsTo<SCTeamJointInfoPacket>(Alice);
        await Assert.That(reply.Count).IsEqualTo(1);
        await Assert.That(reply[0].Info.TargetTeamId).IsEqualTo(TeamB);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
    }

    [Test]
    public async Task Request_TargetMenuSelectionThatIsNotACharacterIsRefused()
    {
        var (manager, world, _) = Build();
        world.AddCharacter(9u, "PartyPaul");
        world.AddTeam(300u, 9u, isParty: true, 9u);
        // A party member is a character but cannot start a joint, so the request is refused on the
        // usual party-target rules rather than on the name being missing.
        world.SelectedTargets[Alice] = 9u;
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, string.Empty, 1);

        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Request_RejectsForeignWorld()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 7);

        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamInviteeOffline);
    }

    [Test]
    public async Task Request_RequiresRaidOwnerOrOfficerOfTheSourceTeam()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Carol, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamNoRights);

        world.SetOfficer(TeamA, Carol);
        manager.RequestJointInfo(Carol, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
    }

    [Test]
    public async Task Request_RejectsPartyTargetAndSelfTarget()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Alice", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);

        world.AddTeam(300u, 9u, isParty: true, 9u);
        world.AddCharacter(9u, "PartyPaul");
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "PartyPaul", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.CountPackets<SCTeamJointInfoPacket>()).IsEqualTo(0);
    }

    [Test]
    public async Task Request_RejectsOverCapacityTeams()
    {
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        world.AddCharacter(Alice, "Alice").AddCharacter(Bob, "Bob");
        var many = Enumerable.Range(1, 30).Select(i => (uint)(100 + i)).ToArray();
        world.AddTeam(TeamA, Alice, false, [Alice, .. many]);
        world.AddTeam(TeamB, Bob, false, [Bob, .. many]);
        var manager = new TeamJointManager(world, clock);

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamFull);
    }

    // ---------- joint accept ----------

    [Test]
    public async Task Accept_CommitsFederationWithLeaderOrdersAndHeaders()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);

        var toBob = world.PacketsTo<SCTeamJointInfoPacket>(Bob);
        await Assert.That(toBob.Count).IsEqualTo(1);
        await Assert.That(manager.SessionCount).IsEqualTo(0);

        // The requester asked for its own raid to lead, so the REQUESTING side leads. The responder's
        // echo is not consulted.
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsTrue();
        await Assert.That(world.Team(TeamA)!.JointOrder).IsEqualTo(1);
        await Assert.That(world.Team(TeamB)!.IsJointLeader).IsFalse();
        await Assert.That(world.Team(TeamB)!.JointOrder).IsEqualTo(2);
        // every online member of both teams gets the republished header and the joint notification
        await Assert.That(world.HeadersSent.Select(entry => entry.RecipientId)).IsEquivalentTo(new[] { Alice, Carol, Bob });
        await Assert.That(world.CountPackets<SCTeamJointPacket>()).IsEqualTo(3);
    }

    [Test]
    public async Task Accept_GivesTheTargetTeamTheLeaderRoleWhenTheRequesterDidNotAskToLead()
    {
        var (manager, world, _) = Build();
        // The requester did not claim the leading side, so the TARGET leads. This is the complement
        // of the requester-leads case: the flag is read in the requester's polarity either way, so
        // clearing it must move the leading role, not merely leave it where it was.
        DriveToResponsePrompt(manager, myTeamLeader: false);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(world.Team(TeamB)!.IsJointLeader).IsTrue();
        await Assert.That(world.Team(TeamB)!.JointOrder).IsEqualTo(1);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsFalse();
        await Assert.That(world.Team(TeamA)!.JointOrder).IsEqualTo(2);
    }

    /// <summary>
    /// The request dialog and the response dialog carry OPPOSITE meanings for the same "leader" key:
    /// the request side is the requester's own choice of which raid leads, while the response side
    /// describes the responder. Reading the requester's stored choice in the response side's polarity
    /// gave the leading role to whichever team asked, so a requester that asked to lead was answered
    /// with the target leading instead. The stored choice is the requester's, and it is read as such.
    /// </summary>
    [Test]
    public async Task Request_RefusesWhenATeamIsAlreadyTheOtherSideOfAnOutstandingAsk()
    {
        // A asks B, then C asks A. Comparing only source-to-source and target-to-target let the
        // second through, leaving one team party to two pending joints and able to be committed into
        // two sessions with its JointId overwritten.
        var (manager, world, _) = Build();

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);

        // Now Bob asks Alice: the same two teams with the sides reversed. Source-to-source and
        // target-to-target both miss, so the narrow check let it through - and one team ended up
        // party to two pending joints.
        world.Errors.Clear();
        manager.RequestJointInfo(Bob, JointType, TeamJointModes.ContextRequest, "Alice", 1);

        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
        await Assert.That(world.Errors.Count).IsEqualTo(1);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamLoading);
    }

    [Test]
    public async Task Accept_CommitsAndTheStoredChoiceDecidesWhoLeads_NotTheEcho()
    {
        var (manager, world, _) = Build();

        // Stored leader choice is true, so the REQUESTING side (Alice's team) leads.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);

        // The responder echoes false, the opposite of the stored value. It must still commit, and the
        // stored choice must still win: the echoed flag is not consulted, because a crafted answer
        // would otherwise be able to pick the owner.
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsTrue();
        await Assert.That(world.Team(TeamA)!.JointOrder).IsEqualTo(1);
        await Assert.That(world.Team(TeamB)!.IsJointLeader).IsFalse();
        await Assert.That(world.Team(TeamB)!.JointOrder).IsEqualTo(2);
    }

    [Test]
    public async Task Accept_LeaderFollowsTheRequestersChoiceWhicheverWayTheResponderEchoes()
    {
        // The key property of the polarity: the requester's stored choice is what decides, and the
        // responder's echo is inert. Both echo values are driven against the same stored choice, so
        // a commit that consulted the echo would give two different answers for one request.
        foreach (var echo in new[] { true, false })
        {
            var (manager, world, _) = Build();
            DriveToResponsePrompt(manager, myTeamLeader: true);

            manager.RespondToJoint(Bob, JointType, myTeamLeader: echo, accept: true, timeout: false);

            await Assert.That(manager.SessionCount).IsEqualTo(1);
            await Assert.That(world.Team(TeamA)!.IsJointLeader).IsTrue();
            await Assert.That(world.Team(TeamB)!.IsJointLeader).IsFalse();
        }
    }

    /// <summary>
    /// A decline is signalled by its own separate boolean, not by an inverted leader flag: the
    /// decline path passes the same leader value the accept path does. So whichever leader value
    /// comes back, a decline must refuse.
    /// </summary>
    [Test]
    public async Task Accept_DeclineStillRefusesWhateverLeaderFlagTheClientEchoes()
    {
        foreach (var echoedLeader in new[] { true, false })
        {
            var (manager, world, _) = Build();
            DriveToResponsePrompt(manager, myTeamLeader: echoedLeader);
            manager.RespondToJoint(Bob, JointType, myTeamLeader: echoedLeader, accept: false, timeout: false);

            await Assert.That(manager.SessionCount).IsEqualTo(0);
            await Assert.That(manager.PendingJointCount).IsEqualTo(0);
            await Assert.That(world.Team(TeamA)!.JointId).IsEqualTo(0u);
        }
    }

    [Test]
    public async Task Accept_RejectsWrongTypeToken()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType + 1, myTeamLeader: true, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
    }

    [Test]
    public async Task Decline_ByPeerClearsPendingAndNotifiesBothSides()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: false, timeout: false);

        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(world.PacketsTo<SCTeamJointPacket>(Alice).Count).IsEqualTo(1);
        await Assert.That(world.PacketsTo<SCTeamJointPacket>(Bob).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Timeout_NotifiesOnlyTheRequester()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: false, timeout: true);

        await Assert.That(world.PacketsTo<SCTeamJointPacket>(Alice).Count).IsEqualTo(1);
        await Assert.That(world.PacketsTo<SCTeamJointPacket>(Bob).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Pending_ExpiresAfterTheRequestLifetime()
    {
        var (manager, world, clock) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);

        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(0);
    }

    [Test]
    public async Task BreakAsk_OutlivesTheJointRequestLifetimeSoTheClientsPromptCanStillBeAnswered()
    {
        // The responder's client keeps the break prompt up for two minutes, so the ask has to be
        // answerable for at least that long. The joint request lifetime is shorter, and an ask
        // closed at that point made an accept inside the client's own window find nothing while the
        // asker was told the break had been rejected.
        var (manager, world, clock) = Build();
        // Alice claims the leading side, so she is the only member who may raise the ask.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Sent.Clear();
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);

        // Past the joint request lifetime, but still inside the break prompt the client is showing.
        clock.Advance(TimeSpan.FromSeconds(90));
        manager.RespondToJointBreak(Bob, ask: false, accept: true);

        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        // The accept landed, so the joint broke instead of the asker being told it was rejected.
        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(world.PacketsTo<SCTeamJointBreakPacket>(Alice)
            .Any(packet => packet.Accept)).IsTrue();
    }

    [Test]
    public async Task BreakAsk_ExpiresAfterTheBreakPromptLifetime()
    {
        // A break ask whose prompt owner never answers would otherwise sit there until the joint is
        // dissolved, refusing every later ask. It lapses with the prompt the client is showing.
        var (manager, world, clock) = Build();
        // Alice claims the leading side, so she is the only member who may raise the ask.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Sent.Clear();
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);

        clock.Advance(TeamJointManager.BreakAskLifetime + TimeSpan.FromSeconds(1));

        // Any later traffic purges it, and the stale ask can no longer dissolve the joint.
        manager.RespondToJointBreak(Bob, ask: false, accept: true);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(1);

        // A fresh ask is accepted again once the stale one is gone.
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);
    }

    [Test]
    public async Task BreakAsk_ExpiredAskTellsTheAskerTheRoundLapsed()
    {
        var (manager, world, clock) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Sent.Clear();
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);

        clock.Advance(TeamJointManager.BreakAskLifetime + TimeSpan.FromSeconds(1));
        // Any later traffic purges the stale ask. The asker must be told, otherwise the prompt it
        // raised stays on screen with nothing behind it.
        manager.RespondToJointBreak(Bob, ask: false, accept: true);

        await Assert.That(world.PacketsTo<SCTeamJointBreakPacket>(Alice).Count).IsEqualTo(1);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
    }

    [Test]
    public async Task AlreadyJointed_TeamCannotStartAnotherRequest()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Errors.Clear();

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamInviteeInTeam);
    }

    // ---------- break ----------

    [Test]
    public async Task Break_AskThenAcceptDissolvesAndRepublishesHeaders()
    {
        var (manager, world, _) = Build();
        // Drive the joint so ALICE's team holds the leader role: the requester claims the leading
        // side, so its own raid leads and only its owner may raise a break ask.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Sent.Clear();
        world.HeadersSent.Clear();

        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);
        await Assert.That(world.PacketsTo<SCTeamJointBreakPacket>(Bob).Count).IsEqualTo(1);
        // count the dissolve fan-out on its own
        world.Sent.Clear();
        world.HeadersSent.Clear();

        manager.RespondToJointBreak(Bob, ask: false, accept: true);
        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamA)!.JointId).IsEqualTo(0u);
        await Assert.That(world.Team(TeamB)!.JointId).IsEqualTo(0u);
        await Assert.That(world.CountPackets<SCTeamJointBreakPacket>()).IsEqualTo(3);
        await Assert.That(world.HeadersSent.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Break_DeclineKeepsTheSessionAndAnswersTheAsker()
    {
        var (manager, world, _) = Build();
        // Drive the joint so ALICE's team holds the leader role: the requester claims the leading
        // side, so its own raid leads and only its owner may raise a break ask.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Sent.Clear();

        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        manager.RespondToJointBreak(Bob, ask: false, accept: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        await Assert.That(world.PacketsTo<SCTeamJointBreakPacket>(Alice).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Break_OnlyTheJointLeaderMayAsk()
    {
        var (manager, world, _) = Build();
        // Drive the joint so ALICE's team holds the leader role: the requester claims the leading
        // side, so its own raid leads and only its owner may raise a break ask.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Errors.Clear();

        manager.RespondToJointBreak(Bob, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamNoRights);

        manager.RespondToJointBreak(Carol, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
    }

    [Test]
    public async Task Break_AnswerWithoutAPendingAskIsIgnored()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        world.Sent.Clear();

        manager.RespondToJointBreak(Bob, ask: false, accept: true);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(world.Sent.Count).IsEqualTo(0);
    }

    // ---------- disband / disconnect cleanup ----------

    [Test]
    public async Task Disband_DissolvesTheJointAndSkipsTheDisbandedTeam()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        world.Sent.Clear();
        world.HeadersSent.Clear();

        manager.OnTeamDisbanded(TeamB);

        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamA)!.JointId).IsEqualTo(0u);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsFalse();
        // only TeamA's two online members are told; the disbanding team is skipped
        await Assert.That(world.HeadersSent.Select(entry => entry.RecipientId)).IsEquivalentTo(new[] { Alice, Carol });
        await Assert.That(world.CountPackets<SCTeamJointBreakPacket>()).IsEqualTo(2);
    }

    [Test]
    public async Task Disband_OfAPendingSideDropsTheRequest()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.OnTeamDisbanded(TeamB);
        world.Sent.Clear();

        manager.RespondToJoint(Bob, JointType, true, true, false);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Disband_DropsPendingSummonsOfThatTeam()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);

        manager.OnTeamDisbanded(TeamA);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
    }

    [Test]
    public async Task Disconnect_MemberOfALiveRaidKeepsTheJointButDropsItsRounds()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        manager.RequestSummons(Alice);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);

        manager.OnCharacterLogout(Carol);

        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disconnect_OwnerWithAnotherMemberLeftHandsOverInsteadOfDissolving()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        world.Sent.Clear();

        // Carol is still in TeamA, so TeamManager would hand ownership to her: the joint survives.
        manager.OnCharacterLogout(Alice);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(world.Team(TeamB)!.JointId).IsNotEqualTo(0u);
    }

    [Test]
    public async Task Disconnect_LastOnlineOwnerOfAMemberTeamDissolvesTheJoint()
    {
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        world.AddCharacter(Alice, "Alice").AddCharacter(Bob, "Bob");
        world.AddTeam(TeamA, Alice, false, Alice);   // Alice is the only member
        world.AddTeam(TeamB, Bob, false, Bob);
        var manager = new TeamJointManager(world, clock);

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        manager.RespondToJoint(Alice, JointType, true, true, false);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
        world.Sent.Clear();
        world.HeadersSent.Clear();

        manager.OnCharacterLogout(Alice);

        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamB)!.JointId).IsEqualTo(0u);
        await Assert.That(world.PacketsTo<SCTeamJointBreakPacket>(Bob).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Disconnect_OfASummonerDropsTheRoundItOpened()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);

        manager.OnCharacterLogout(Alice);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
    }

    [Test]
    public async Task Disconnect_OfACharacterWithNoTeamIsInert()
    {
        var (manager, world, _) = Build();
        world.AddCharacter(77u, "Lonely");
        manager.OnCharacterLogout(77u);
        manager.OnCharacterLogout(0u);
        manager.OnTeamDisbanded(0u);

        await Assert.That(manager.SessionCount).IsEqualTo(0);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
    }

    [Test]
    public async Task Disconnect_OfAPlainMemberDoesNotCancelTheOwnersPendingRequest()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);

        // Carol never opened this request; her logout must leave Alice's handshake alone.
        manager.OnCharacterLogout(Carol);

        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
        // and the owner's handshake still completes normally
        manager.RespondToJoint(Alice, JointType, myTeamLeader: true, accept: true, timeout: false);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disconnect_OfTheRequestingOwnerCancelsTheirOwnPendingRequest()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);

        manager.OnCharacterLogout(Alice);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);

        // the requester coming back afterwards has nothing to answer either
        manager.RespondToJoint(Alice, JointType, myTeamLeader: true, accept: true, timeout: false);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
    }

    [Test]
    public async Task Disconnect_OfTheJointLeaderClearsTheirOwnPendingBreakAsk()
    {
        var (manager, world, _) = Build();
        // Drive the joint so ALICE's team holds the leader role: the requester claims the leading
        // side, so its own raid leads and only its owner may raise a break ask.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);
        world.Sent.Clear();

        // Alice is the joint leader and owns the ask; her logout must retract it.
        manager.OnCharacterLogout(Alice);

        await Assert.That(manager.PendingBreakCount).IsEqualTo(0);
        await Assert.That(manager.SessionCount).IsEqualTo(1);

        // the peer can no longer dissolve through the retracted ask
        manager.RespondToJointBreak(Bob, ask: false, accept: true);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(world.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Disconnect_OfThePeerDoesNotClearTheLeadersPendingBreakAsk()
    {
        // Bob's raid needs a second member, otherwise his logout disbands it and the joint (and the
        // break ask) legitimately go with it. Dan stays online so the federation survives Bob.
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var world = new FakeTeamJointContext { LocalWorldId = 1 };
        world.AddCharacter(Alice, "Alice").AddCharacter(Bob, "Bob").AddCharacter(4u, "Dan");
        world.AddTeam(TeamA, Alice, false, Alice, Carol);
        world.AddTeam(TeamB, Bob, false, Bob, 4u);
        var manager = new TeamJointManager(world, clock);

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.ContextRequest, "Bob", 1);
        // The requester claims the leading side, so ALICE's team is the joint leader and only she may
        // raise the ask. The echoed flag is not consulted.
        manager.RespondToJoint(Alice, JointType, true, true, false);
        manager.RespondToJoint(Bob, JointType, true, true, false);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsTrue();
        manager.RespondToJointBreak(Alice, ask: true, accept: false);
        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);
        await Assert.That(manager.SessionCount).IsEqualTo(1);

        // Bob never raised the ask, so his logout must not retract it.
        manager.OnCharacterLogout(Bob);

        await Assert.That(manager.PendingBreakCount).IsEqualTo(1);
        await Assert.That(manager.SessionCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disconnect_OfARecipientDropsTheSummonRoundTargetingThem()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);

        // Carol is the recipient: her logout must close the round addressed to her.
        manager.OnCharacterLogout(Carol);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
    }

    // ---------- summon ----------

    [Test]
    public async Task Summon_SuggestsToOnlineTeammatesAndListsThemForTheSummoner()
    {
        var (manager, world, _) = Build();
        var ids = manager.RequestSummons(Alice);

        await Assert.That(ids).IsEquivalentTo(new[] { Carol });
        await Assert.That(world.PacketsTo<SCTeamSummonSuggestPacket>(Carol).Count).IsEqualTo(1);
        await Assert.That(world.PacketsTo<SCTeamSummonGetPacket>(Alice).Count).IsEqualTo(1);
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);
    }

    [Test]
    public async Task Summon_IsOwnerOnly()
    {
        var (manager, world, _) = Build();
        world.Errors.Clear();
        var ids = manager.RequestSummons(Carol);

        await Assert.That(ids).IsEmpty();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamNoRights);
    }

    [Test]
    public async Task Summon_AcceptEmitsTheConsentPacketExactlyOnce()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);

        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsTrue();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(world.PacketsTo<SCTeamSummonPacket>(Carol).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Summon_DuplicateAcceptIsRefusedAndEmitsNothing()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);

        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsTrue();
        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(world.PacketsTo<SCTeamSummonPacket>(Carol).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Summon_RejectConsumesTheRoundAndALaterAcceptFails()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);

        await Assert.That(manager.ReplyToSummon(Carol, false, "Alice")).IsTrue();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
        await Assert.That(world.CountPackets<SCTeamSummonPacket>()).IsEqualTo(0);
    }

    [Test]
    public async Task Summon_ReplyMustMatchTheSummonerName()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);

        await Assert.That(manager.ReplyToSummon(Carol, true, "SomeoneElse")).IsFalse();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);
    }

    [Test]
    public async Task Summon_AcceptIsRefusedWhileInCombat()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);
        world.SetInBattle(Carol, true);

        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
        await Assert.That(world.CountPackets<SCTeamSummonPacket>()).IsEqualTo(0);
    }

    [Test]
    public async Task Summon_AcceptIsRefusedWhenTheSummonerWentOffline()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);
        world.GoOffline(Alice);

        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
        await Assert.That(world.CountPackets<SCTeamSummonPacket>()).IsEqualTo(0);
    }

    [Test]
    public async Task Summon_ExpiresWithTheRequestLifetime()
    {
        var (manager, world, clock) = Build();
        manager.RequestSummons(Alice);
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        await Assert.That(manager.ReplyToSummon(Carol, true, "Alice")).IsFalse();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(0);
    }

    [Test]
    public async Task Summon_DoesNotReopenForATargetAlreadyPending()
    {
        var (manager, world, _) = Build();
        manager.RequestSummons(Alice);
        world.Sent.Clear();

        var ids = manager.RequestSummons(Alice);
        await Assert.That(ids).IsEmpty();
        await Assert.That(manager.PendingSummonCount).IsEqualTo(1);
    }
}
