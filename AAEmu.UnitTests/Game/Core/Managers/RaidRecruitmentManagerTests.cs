using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Team.Recruitment;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class RaidRecruitmentManagerTests
{
    private const long PostCreateTime = 1_800_000_000;
    private const long PostExpireTime = 1_800_003_600;

    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;
        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly (FieldInfo Field, object Previous)[] _replaced;

        public RaidRecruitmentManager Manager { get; }
        public TeamManager Teams { get; }
        public Dictionary<uint, RaidRecruitment> Posts { get; }
        public Mock<ITeamIdManager> TeamIds { get; }

        public Fixture()
        {
            TeamIds = Mock.Of<ITeamIdManager>();
            TeamIds.GetNextId().Returns(1u);
            var chat = Mock.Of<IChatManager>();
            chat.GetPartyChat(Any<Team>(), Any<Character>()).Returns(new ChatChannel());
            chat.GetRaidChat(Any<Team>()).Returns(new ChatChannel());
            Teams = new TeamManager(Mock.Of<IWorldManager>().Object, chat.Object,
                TeamIds.Object, Mock.Of<ITickManager>().Object);
            Replace<TeamManager>(Teams);
            Replace<FriendMananger>(new FriendMananger());
            // Taken after the singletons are in place: RaidRecruitmentManager.Instance must be the one the
            // board resolves, and its post table the one the tests seed.
            _replaced = [Replace(RaidRecruitmentManager.Instance)];
            Manager = RaidRecruitmentManager.Instance;
            Posts = (Dictionary<uint, RaidRecruitment>)typeof(RaidRecruitmentManager)
                .GetField("_byOwner", Fields)!.GetValue(Manager)!;
        }

        public Team AddTeam(uint teamId, bool isParty, params Character[] members)
        {
            var team = new Team { Id = teamId, OwnerId = members[0].Id, IsParty = isParty };
            foreach (var member in members)
                team.AddMember(member);
            var active = (ConcurrentDictionary<uint, Team>)typeof(TeamManager)
                .GetField("_activeTeams", Fields)!.GetValue(Teams)!;
            active[teamId] = team;
            return team;
        }

        public RaidRecruitment AddPost(Character owner, int headcount = 5, bool autoJoin = false)
        {
            var entry = new RaidRecruitment
            {
                Owner = owner,
                Headcount = (uint)headcount,
                AutoJoin = autoJoin,
                CreateTime = PostCreateTime,
                ExpireTime = PostExpireTime
            };
            Posts[owner.Id] = entry;
            return entry;
        }

        public void Dispose()
        {
            // The instance is handed back to the next test through the singleton field, so it goes back empty.
            Posts.Clear();
            foreach (var (field, previous) in _replaced)
                field.SetValue(null, previous);
        }

        private static (FieldInfo Field, object Previous) Replace<T>(T instance) where T : class
        {
            var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previous = field.GetValue(null);
            field.SetValue(null, instance);
            return (field, previous);
        }
    }

    private static Character CreateCharacter(uint id, string name, RecordingSession session = null)
    {
        // IsOnline is assigned while a connection exists, so the board's own "poster logged off" sweep
        // does not delete the seeded post.
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = name };
        character.Connection = new GameConnection(session ?? new RecordingSession()) { ActiveChar = character };
        character.IsOnline = true;
        return character;
    }

    private static string[] Names(RaidRecruitment entry)
    {
        var names = new string[entry.Applicants.Count];
        for (var i = 0; i < entry.Applicants.Count; i++)
            names[i] = entry.Applicants[i].Character.Name;
        return names;
    }

    // Auto-Invite approves on the spot, but only while the post has a free seat: with headcount 5 and a
    // solo poster, four applicants are approved and the rest wait for the recruiter. Approving every
    // applicant used to turn the accept popup into a TEAM_FULL for whoever answered first.
    [Test]
    public async Task AutoJoin_ApprovesOnlyTheOpenSeatsAndLeavesTheRestPending()
    {
        using var fixture = new Fixture();
        var poster = CreateCharacter(9, "Poster", new RecordingSession());
        fixture.AddPost(poster, headcount: 5, autoJoin: true);
        var sessions = new List<RecordingSession>();
        for (uint i = 0; i < 5; i++)
        {
            var session = new RecordingSession();
            var applicant = CreateCharacter(20 + i, $"Applicant{i + 1}", session);
            fixture.Manager.Apply(applicant, poster.Id, (uint)MemberRole.Tank, PostCreateTime);
            sessions.Add(session);
        }

        var entry = fixture.Posts[poster.Id];
        await Assert.That(Names(entry)).IsEquivalentTo(["Applicant1", "Applicant2", "Applicant3", "Applicant4", "Applicant5"]);
        await Assert.That(entry.Applicants[0].State).IsEqualTo(RaidApplicantState.Accepted);
        await Assert.That(entry.Applicants[1].State).IsEqualTo(RaidApplicantState.Accepted);
        await Assert.That(entry.Applicants[2].State).IsEqualTo(RaidApplicantState.Accepted);
        await Assert.That(entry.Applicants[3].State).IsEqualTo(RaidApplicantState.Accepted);
        await Assert.That(entry.Applicants[4].State).IsEqualTo(RaidApplicantState.Pending);
        await Assert.That(entry.AcceptedPendingCount).IsEqualTo(4);
        // Every applicant hears back, whether they were approved or left waiting.
        foreach (var session in sessions)
            await Assert.That(session.Packets.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task AutoJoin_SeatsTheConfirmingApplicantInTheNewTeam()
    {
        using var fixture = new Fixture();
        var poster = CreateCharacter(9, "Poster", new RecordingSession());
        var applicant = CreateCharacter(20, "Applicant", new RecordingSession());
        var entry = fixture.AddPost(poster, headcount: 5, autoJoin: true);

        fixture.Manager.Apply(applicant, poster.Id, (uint)MemberRole.Healer, PostCreateTime);
        await Assert.That(entry.Applicants[0].State).IsEqualTo(RaidApplicantState.Accepted);

        fixture.Manager.AcceptReply(applicant, poster.Id, join: true, (uint)MemberRole.Healer);

        var team = fixture.Teams.GetActiveTeamByUnit(poster.Id);
        await Assert.That(team).IsNotNull();
        await Assert.That(team!.IsMember(applicant.Id)).IsTrue();
        await Assert.That(entry.TeamId).IsEqualTo(team.Id);
        await Assert.That(entry.Applicants).IsEmpty();
    }

    [Test]
    public async Task Accept_RefusesTheApplicantBeyondTheHeadcount()
    {
        using var fixture = new Fixture();
        var poster = CreateCharacter(9, "Poster", new RecordingSession());
        var first = CreateCharacter(20, "First", new RecordingSession());
        var second = CreateCharacter(21, "Second", new RecordingSession());
        var entry = fixture.AddPost(poster, headcount: 2, autoJoin: true);

        fixture.Manager.Apply(first, poster.Id, (uint)MemberRole.Tank, PostCreateTime);
        fixture.Manager.Apply(second, poster.Id, (uint)MemberRole.Tank, PostCreateTime);
        await Assert.That(entry.Applicants[0].State).IsEqualTo(RaidApplicantState.Accepted);
        await Assert.That(entry.Applicants[1].State).IsEqualTo(RaidApplicantState.Pending);

        fixture.Manager.Accept(poster, poster.Id, [second.Id]);

        await Assert.That(entry.Applicants[1].State).IsEqualTo(RaidApplicantState.Pending);
    }

    // A post made while solo has no team until it is bound. Binding it to a team the poster cannot invite
    // from would leave TryAddRecruitedMember answering TEAM_FULL, so the post is deleted instead.
    [Test]
    public async Task SoloPost_IsDeletedWhenItsPosterJoinsATeamTheyCannotRecruitFor()
    {
        using var fixture = new Fixture();
        var leader = CreateCharacter(10, "Leader", new RecordingSession());
        var poster = CreateCharacter(9, "Poster", new RecordingSession());
        var team = fixture.AddTeam(1, isParty: false, leader);
        var entry = fixture.AddPost(poster, headcount: 5);
        entry.Applicants.Add(new RaidApplicant(poster, (uint)MemberRole.Tank, PostCreateTime));

        // Before the team takes them the post is still solo; only the join may bind or delete it.
        fixture.Manager.OnMemberJoined(team, poster);
        await Assert.That(fixture.Posts.ContainsKey(poster.Id)).IsTrue();

        team.AddMember(poster);
        fixture.Manager.OnMemberJoined(team, poster);

        await Assert.That(fixture.Posts).IsEmpty();
        await Assert.That(entry.Applicants).IsEmpty();
    }

    // Binding also needs the post to still fit: a team that already fills the headcount cannot seat anyone.
    [Test]
    public async Task SoloPost_DeletedWhenTheTeamAlreadyHasNoSeatOrItsOwnPost()
    {
        using var fixture = new Fixture();
        var poster = CreateCharacter(9, "Poster", new RecordingSession());
        var entry = fixture.AddPost(poster, headcount: 3);
        var team = fixture.AddTeam(1, isParty: false, poster);
        team.AddMember(CreateCharacter(10, "Member1"));
        team.AddMember(CreateCharacter(11, "Member2"));

        fixture.Manager.OnMemberJoined(team, poster);

        await Assert.That(fixture.Posts).IsEmpty();
        await Assert.That(entry.Applicants).IsEmpty();

        // The same team posting for itself leaves no room for a second post on the board.
        var leader = CreateCharacter(12, "Leader", new RecordingSession());
        var joiner = CreateCharacter(13, "Joiner", new RecordingSession());
        var teamPost = fixture.AddPost(leader, headcount: 10);
        teamPost.TeamId = 2;
        fixture.AddPost(joiner, headcount: 5);
        var second = fixture.AddTeam(2, isParty: false, leader, joiner);

        fixture.Manager.OnMemberJoined(second, joiner);

        await Assert.That(fixture.Posts.Count).IsEqualTo(1);
        await Assert.That(fixture.Posts.ContainsKey(leader.Id)).IsTrue();
        await Assert.That(fixture.Posts.ContainsKey(joiner.Id)).IsFalse();
    }
}
