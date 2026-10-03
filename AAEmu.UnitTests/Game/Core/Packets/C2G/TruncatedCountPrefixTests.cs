using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Team.Recruitment;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Bodies whose first field is an element count. Two things go wrong when such a count is read from
/// a short body: the count degrades to 0, which a <c>LeftBytes</c> bound cannot tell apart from a
/// client that really sent 0 (0 is never greater than 0/8), and a count that did arrive can be
/// arbitrarily large, which turns the element loop into a resource sink.
/// </summary>
[NotInParallel]
public class TruncatedCountPrefixTests
{
    private const ulong OwnerId = 0x1122334455667788;
    private const ulong ApplicantA = 0x21;
    private const ulong ApplicantB = 0x22;
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

    private sealed class RaidFixture : IDisposable
    {
        public RaidRecruitmentManager Manager { get; }
        public Dictionary<uint, RaidRecruitment> Posts { get; }
        public Character Poster { get; }

        public RaidFixture()
        {
            var teamIds = Mock.Of<ITeamIdManager>();
            teamIds.GetNextId().Returns(1u);
            var chat = Mock.Of<IChatManager>();
            chat.GetPartyChat(Any<Team>(), Any<Character>()).Returns(new ChatChannel());
            chat.GetRaidChat(Any<Team>()).Returns(new ChatChannel());
            var teams = new TeamManager(Mock.Of<IWorldManager>().Object, chat.Object, teamIds.Object,
                Mock.Of<ITickManager>().Object);
            // SingletonScope rather than a hand-rolled s_instance write: same one-field shadow, but it
            // only puts the previous value back if nothing else installed a value meanwhile, and it
            // restores on dispose. No ServiceProvider is replaced anywhere in this file - that swap is
            // process-wide and can make an unrelated class resolve a fresh, empty singleton.
            _scopes.Add(new SingletonScope<TeamManager>(teams));
            _scopes.Add(new SingletonScope<FriendMananger>(new FriendMananger()));
            _scopes.Add(new SingletonScope<RaidRecruitmentManager>(RaidRecruitmentManager.Instance));

            Manager = RaidRecruitmentManager.Instance;
            Posts = (Dictionary<uint, RaidRecruitment>)typeof(RaidRecruitmentManager)
                .GetField("_byOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Manager)!;

            Poster = Online(9, "Poster");
            Posts[Poster.Id] = new RaidRecruitment
            {
                Owner = Poster,
                Headcount = 5,
                CreateTime = PostCreateTime,
                ExpireTime = PostExpireTime,
            };
        }

        private readonly List<IDisposable> _scopes = [];

        public Character Applicant(uint id, string name) => Online(id, name);

        public void Dispose()
        {
            Posts.Clear();
            for (var i = _scopes.Count - 1; i >= 0; i--)
                _scopes[i].Dispose();
        }

        private static Character Online(uint id, string name)
        {
            // IsOnline is assigned while a connection exists, so the board's own "poster logged off"
            // sweep does not delete the seeded post.
            var character = new CharacterMock { Id = id, ObjId = id, Name = name };
            character.Connection = new GameConnection(new RecordingSession()) { ActiveChar = character };
            typeof(Character).GetField("_isOnline", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(character, true);
            return character;
        }

    }

    // ------------------------------------------------------------------ Overran before LeftBytes

    [Test]
    public async Task ReadCharacterIds_CompleteList_IsParsed()
    {
        // The negative control: with the count and the ids both present the list comes back, so a
        // refusal below is about the missing count and not about the guard refusing everything.
        using var fixture = new RaidFixture();

        var ids = CSRaidApplicantAcceptPacket.ReadCharacterIds(Ids(ApplicantA, ApplicantB), 2);

        await Assert.That(ids).IsEquivalentTo(new[] { ApplicantA, ApplicantB });
    }

    [Test]
    public async Task ReadCharacterIds_CountPresentButOwnerIdMissing_RefusesWithTheTruncationReason()
    {
        using var fixture = new RaidFixture();

        // The count read succeeds and the owner-id read does not, so both guards would fire here: the
        // count of 5 against nothing left fails the LeftBytes bound, and the stream has also recorded
        // an overrun. Asserting the reason is what pins the order - the truncation has to be named
        // first, because "the count exceeds the body" describes a body that was simply cut short, not
        // a client that asked for more than it sent.
        var body = new PacketStream().Write(5u);
        _ = body.ReadUInt32(); // the packet's count read: succeeds
        _ = body.ReadUInt64(); // its owner-id read: overruns

        var threw = Capture(() => CSRaidApplicantAcceptPacket.ReadCharacterIds(body, 5));

        await Assert.That(threw).IsNotNull();
        await Assert.That(threw!.Message).Contains("missing from the body");
    }

    [Test]
    public async Task ReadCharacterIds_CountNeverArrived_IsRefused()
    {
        using var fixture = new RaidFixture();

        // The case LeftBytes cannot see at all. The packet's own count read overruns, answers 0, and
        // pins Pos at the end, so LeftBytes is 0 and 0 > 0/8 is false: the bound passes and the caller
        // would be handed an empty list as a deliberate "no applicants". Only the Overran check stops
        // this, so deleting it - rather than moving it - is the mutation this case catches.
        var threw = Capture(() => CSRaidApplicantAcceptPacket.ReadCharacterIds(AfterHeaderReads(), 0));

        await Assert.That(threw).IsNotNull();
    }

    [Test]
    public async Task ApplicantAccept_TruncatedCount_LeavesTheApplicantsPending()
    {
        using var fixture = new RaidFixture();
        var first = fixture.Applicant(20, "First");
        var second = fixture.Applicant(21, "Second");
        fixture.Manager.Apply(first, fixture.Poster.Id, (uint)MemberRole.Tank, PostCreateTime);
        fixture.Manager.Apply(second, fixture.Poster.Id, (uint)MemberRole.Healer, PostCreateTime);
        var entry = fixture.Posts[fixture.Poster.Id];

        var threw = Try(() => new CSRaidApplicantAcceptPacket { Connection = fixture.Poster.Connection }
            .Decode(Truncated()));

        await Assert.That(threw).IsTrue();
        await Assert.That(entry.Applicants[0].State).IsEqualTo(RaidApplicantState.Pending);
        await Assert.That(entry.Applicants[1].State).IsEqualTo(RaidApplicantState.Pending);
    }

    [Test]
    public async Task ApplicantAccept_CompleteBody_AcceptsTheApplicants()
    {
        // The negative control for the case above: the same manager, the same post, a body that
        // carries the count and the ids does move the applicants. The actor is the poster, because
        // that is who the board resolves the post for.
        using var fixture = new RaidFixture();
        var first = fixture.Applicant(20, "First");
        fixture.Manager.Apply(first, fixture.Poster.Id, (uint)MemberRole.Tank, PostCreateTime);
        var entry = fixture.Posts[fixture.Poster.Id];

        new CSRaidApplicantAcceptPacket { Connection = fixture.Poster.Connection }
            .Decode(Body(1u, fixture.Poster.Id, first.Id));

        await Assert.That(entry.Applicants[0].State).IsNotEqualTo(RaidApplicantState.Pending);
    }

    [Test]
    public async Task ApplicantReject_TruncatedCount_LeavesTheApplicantsPending()
    {
        // The reject twin shares the reader, so it shares the refusal.
        using var fixture = new RaidFixture();
        var first = fixture.Applicant(20, "First");
        fixture.Manager.Apply(first, fixture.Poster.Id, (uint)MemberRole.Tank, PostCreateTime);
        var entry = fixture.Posts[fixture.Poster.Id];

        var threw = Try(() => new CSRaidApplicantRejectPacket { Connection = fixture.Poster.Connection }
            .Decode(Truncated()));

        await Assert.That(threw).IsTrue();
        await Assert.That(entry.Applicants[0].State).IsEqualTo(RaidApplicantState.Pending);
    }

    // ------------------------------------------------------------------ an unbounded count

    [Test]
    public async Task RandomShopGoodsBuy_HugeCount_StopsAtTheEndOfTheBody()
    {
        // shopType, bc npc, bc doodad, u32 type, bool, then the requested-offer count.
        var body = new PacketStream()
            .Write((byte)0)
            .WriteBc(1)
            .WriteBc(2)
            .Write(0u)
            .Write(false)
            .Write(uint.MaxValue);
        var packet = new CSRandomShopGoodsBuyPacket { Connection = new GameConnection(Mock.Of<ISession>().Object) };

        // Pre-fix this looped size times, appending a zero per pass: a 17 byte body asking for four
        // billion offers. With the body armed, the first read past the end stops it.
        body.RequireComplete();
        var elapsed = Stopwatch.StartNew();
        var threw = Try(() => packet.Decode(body));
        elapsed.Stop();

        await Assert.That(threw).IsTrue();
        await Assert.That(packet.RequestedGoods.Count).IsLessThan(64);
        await Assert.That(elapsed.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RandomShopGoodsBuy_CompleteCount_IsParsed()
    {
        // The negative control for the case above: a count the body can satisfy is read in full.
        var body = new PacketStream()
            .Write((byte)0)
            .WriteBc(1)
            .WriteBc(2)
            .Write(0u)
            .Write(false)
            .Write(2u)
            .Write(11u)
            .Write(12u);
        var packet = new CSRandomShopGoodsBuyPacket { Connection = new GameConnection(Mock.Of<ISession>().Object) };

        body.RequireComplete();
        var threw = Try(() => packet.Decode(body));

        await Assert.That(packet.RequestedGoods).IsEquivalentTo(new List<int> { 11, 12 });
        // Whatever the handler decided about a pack it cannot resolve, the refusal is not the parse.
        await Assert.That(threw is TruncatedPacketException).IsFalse();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Just the u64 id list, positioned where the reader starts reading it.</summary>
    private static PacketStream Ids(params ulong[] ids)
    {
        var body = new PacketStream();
        foreach (var id in ids)
            body.Write(id);
        return body;
    }

    /// <summary>The whole wire body: u32 count, u64 owner id, then one u64 per applicant.</summary>
    private static PacketStream Body(uint count, ulong ownerId, params ulong[] ids)
    {
        var body = new PacketStream().Write(count).Write(ownerId);
        foreach (var id in ids)
            body.Write(id);
        return body;
    }

    /// <summary>
    /// A body whose count and owner-id reads have already overrun, the state
    /// <c>ReadCharacterIds</c> is entered in by a truncated packet: Pos pinned at Count, so
    /// LeftBytes is 0 and the Overran flag is what is left to go on.
    /// </summary>
    private static PacketStream AfterHeaderReads()
    {
        var body = new PacketStream().Write((byte)0x01);
        _ = body.ReadUInt32(); // the packet's count read: overruns
        _ = body.ReadUInt64(); // and its owner id read
        return body;
    }

    /// <summary>A body that ends before the u32 count is complete.</summary>
    private static PacketStream Truncated() => new PacketStream().Write((byte)0x01);

    private static bool Try(Action action) => Capture(action) != null;

    /// <summary>
    /// Runs the action and hands back what it threw. A case that only asks "did it refuse" cannot tell
    /// one guard from another, and the order of the two guards in the reader is the thing under test.
    /// </summary>
    private static Exception Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
