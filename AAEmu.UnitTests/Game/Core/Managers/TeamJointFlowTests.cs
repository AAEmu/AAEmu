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
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        manager.RespondToJoint(Alice, JointType, myTeamLeader, true, false);
    }

    // ---------- joint request ----------

    [Test]
    public async Task Request_AnswersRequesterWithModeThreeAndOpensPending()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);

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
        // Mode 3 is TEAM_JOINT_REQUEST: the raid popup's "invite raid joint" entry sends it
        // (x2ui/components/popup_menu_proc.lua:236), so it must be accepted as a request too.
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

        // Mode 2 with no name and nothing selected is still refused: there is nothing to resolve.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuTargetRequest, string.Empty, 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);

        // Mode 1 carries the name, so a blank one is refused the same way.
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "  ", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.All(entry => entry.Error == ErrorMessageType.TeamInviteeOffline)).IsTrue();

        // Mode 2 with no name now resolves through the requester's current selection.
        world.SelectedTargets[Alice] = Bob;
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuTargetRequest, string.Empty, 1);

        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
        await Assert.That(world.PacketsTo<SCTeamJointInfoPacket>(Alice).Count).IsEqualTo(1);
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
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuTargetRequest, string.Empty, 1);

        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Request_RejectsForeignWorld()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 7);

        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamInviteeOffline);
    }

    [Test]
    public async Task Request_RequiresRaidOwnerOrOfficerOfTheSourceTeam()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Carol, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamNoRights);

        world.SetOfficer(TeamA, Carol);
        manager.RequestJointInfo(Carol, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
    }

    [Test]
    public async Task Request_RejectsPartyTargetAndSelfTarget()
    {
        var (manager, world, _) = Build();
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Alice", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);

        world.AddTeam(300u, 9u, isParty: true, 9u);
        world.AddCharacter(9u, "PartyPaul");
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "PartyPaul", 1);
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

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
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

        // The responder echoes leader == true, and in the response dialog's polarity that means
        // the responder is the owner, so the TARGET side leads.
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamB)!.IsJointLeader).IsTrue();
        await Assert.That(world.Team(TeamB)!.JointOrder).IsEqualTo(1);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsFalse();
        await Assert.That(world.Team(TeamA)!.JointOrder).IsEqualTo(2);
        // every online member of both teams gets the republished header and the joint notification
        await Assert.That(world.HeadersSent.Select(entry => entry.RecipientId)).IsEquivalentTo(new[] { Alice, Carol, Bob });
        await Assert.That(world.CountPackets<SCTeamJointPacket>()).IsEqualTo(3);
    }

    [Test]
    public async Task Accept_GivesTheSourceTeamTheLeaderRoleWhenTheResponderIsNotTheOwner()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: false);
        // leader == false in the response dialog means the responder is the officer, so the
        // requester's team keeps the owner role and leads.
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsTrue();
        await Assert.That(world.Team(TeamA)!.JointOrder).IsEqualTo(1);
        await Assert.That(world.Team(TeamB)!.IsJointLeader).IsFalse();
        await Assert.That(world.Team(TeamB)!.JointOrder).IsEqualTo(2);
    }

    /// <summary>
    /// The regression this branch fixes. The two sides of the exchange carry OPPOSITE meanings for
    /// the same "leader" key — the request dialog means "the requester is the officer"
    /// (handle_task.lua:2829/:2836) and the response dialog means "the responder is the owner"
    /// (handle_task.lua:2883/:2890, joint_view.lua:462-463). The old equality test compared the
    /// two directly and so refused a genuine accept whenever the two sides held the same role.
    /// The old tests hid this by echoing the SAME flag on both sides, so drive the two sides with
    /// different values here and assert the commit succeeds and picks the documented leader.
    /// </summary>
    [Test]
    public async Task Request_RefusesWhenATeamIsAlreadyTheOtherSideOfAnOutstandingAsk()
    {
        // A asks B, then C asks A. Comparing only source-to-source and target-to-target let the
        // second through, leaving one team party to two pending joints and able to be committed into
        // two sessions with its JointId overwritten.
        var (manager, world, _) = Build();

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);

        // Now Bob asks Alice: the same two teams with the sides reversed. Source-to-source and
        // target-to-target both miss, so the narrow check let it through - and one team ended up
        // party to two pending joints.
        world.Errors.Clear();
        manager.RequestJointInfo(Bob, JointType, TeamJointModes.MenuChatRequest, "Alice", 1);

        await Assert.That(manager.PendingJointCount).IsEqualTo(1);
        await Assert.That(world.Errors.Count).IsEqualTo(1);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamLoading);
    }

    [Test]
    public async Task Accept_CommitsAndTheStoredChoiceDecidesWhoLeads_NotTheEcho()
    {
        var (manager, world, _) = Build();

        // Stored leader choice is true, so the TARGET side (Bob) leads.
        DriveToResponsePrompt(manager, myTeamLeader: true);
        await Assert.That(manager.PendingJointCount).IsEqualTo(1);

        // The responder echoes false, the opposite of the stored value. It must still commit, and the
        // stored choice must still win: the echoed flag is not consulted, because a crafted answer
        // would otherwise be able to pick the owner.
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);

        await Assert.That(manager.SessionCount).IsEqualTo(1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Team(TeamB)!.IsJointLeader).IsTrue();
        await Assert.That(world.Team(TeamB)!.JointOrder).IsEqualTo(1);
        await Assert.That(world.Team(TeamA)!.IsJointLeader).IsFalse();
        await Assert.That(world.Team(TeamA)!.JointOrder).IsEqualTo(2);
    }

    /// <summary>
    /// A decline is signalled by JointCancel's SEPARATE boolean, not by an inverted leader flag
    /// (handle_task.lua:2913 passes the same infoTable["leader"] as OkProc does at :2910). So
    /// whichever leader value comes back, accept == false must refuse.
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
    public async Task AlreadyJointed_TeamCannotStartAnotherRequest()
    {
        var (manager, world, _) = Build();
        DriveToResponsePrompt(manager, myTeamLeader: true);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: true, accept: true, timeout: false);
        world.Errors.Clear();

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        await Assert.That(manager.PendingJointCount).IsEqualTo(0);
        await Assert.That(world.Errors.Single().Error).IsEqualTo(ErrorMessageType.TeamInviteeInTeam);
    }

    // ---------- break ----------

    [Test]
    public async Task Break_AskThenAcceptDissolvesAndRepublishesHeaders()
    {
        var (manager, world, _) = Build();
        // Drive the joint so ALICE's team holds the owner role: in the response dialog's
        // polarity leader == false means the responder is the officer, so the requester leads.
        DriveToResponsePrompt(manager, myTeamLeader: false);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);
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
        // Drive the joint so ALICE's team holds the owner role: in the response dialog's
        // polarity leader == false means the responder is the officer, so the requester leads.
        DriveToResponsePrompt(manager, myTeamLeader: false);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);
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
        // Drive the joint so ALICE's team holds the owner role: in the response dialog's
        // polarity leader == false means the responder is the officer, so the requester leads.
        DriveToResponsePrompt(manager, myTeamLeader: false);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);
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

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
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
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
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
        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
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
        // Drive the joint so ALICE's team holds the owner role: in the response dialog's
        // polarity leader == false means the responder is the officer, so the requester leads.
        DriveToResponsePrompt(manager, myTeamLeader: false);
        manager.RespondToJoint(Bob, JointType, myTeamLeader: false, accept: true, timeout: false);
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

        manager.RequestJointInfo(Alice, JointType, TeamJointModes.MenuChatRequest, "Bob", 1);
        // Stored leader choice is false, so the SOURCE side (Alice) leads. The echoed flag is no
        // longer consulted; the ask below must come from the joint leader.
        manager.RespondToJoint(Alice, JointType, false, true, false);
        manager.RespondToJoint(Bob, JointType, false, true, false);
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
