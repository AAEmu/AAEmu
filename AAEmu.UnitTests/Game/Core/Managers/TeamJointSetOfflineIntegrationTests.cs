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
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using Microsoft.Extensions.DependencyInjection;

using GameTeam = AAEmu.Game.Models.Game.Team.Team;
using TeamJointModes = AAEmu.Game.Models.Game.Team.TeamJointModes;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Exercises the production wiring: setting <c>Character.IsOnline = false</c> runs
/// <c>TeamManager.SetOffline</c>, which must reach the joint manager. The flow tests call
/// <c>OnCharacterLogout</c> directly and cannot prove that hook is actually wired.
/// </summary>
[NotInParallel]
public class TeamJointSetOfflineIntegrationTests
{
    private const uint Alice = 1u;
    private const uint Carol = 3u;
    private const uint RaidTeamId = 100u;

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

    /// <summary>Swaps the private static singleton slots the IsOnline path reaches through.</summary>
    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Slot = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<(Type Type, FieldInfo Field, object? Previous)> _slots = [];

        public TeamManager Teams { get; }
        public TeamJointManager Joints { get; }
        public FakeTeamJointContext World { get; } = new() { LocalWorldId = 1 };

        public Fixture()
        {
            Teams = new TeamManager(
                Mock.Of<IWorldManager>().Object,
                Mock.Of<IChatManager>().Object,
                Mock.Of<ITeamIdManager>().Object,
                Mock.Of<ITickManager>().Object);
            Set<TeamManager>(Teams);
            Set<FriendMananger>(new FriendMananger());
            Set<RaidRecruitmentManager>(new RaidRecruitmentManager());
            Set<SquadManager>(new SquadManager());

            Joints = new TeamJointManager(World);
            var services = new ServiceCollection();
            services.AddSingleton(Joints);
            var previousProvider = SingletonContainer.ServiceProvider;
            SingletonContainer.ServiceProvider = services.BuildServiceProvider();
            _slots.Add((typeof(TeamJointManager),
                typeof(Singleton<TeamJointManager>).GetField("s_instance", Slot)!, previousProvider));
        }

        private void Set<T>(T instance) where T : class
        {
            var field = typeof(Singleton<T>).GetField("s_instance", Slot)!;
            _slots.Add((typeof(T), field, field.GetValue(null)));
            field.SetValue(null, instance);
        }

        public ConcurrentDictionary<uint, GameTeam> ActiveTeams =>
            (ConcurrentDictionary<uint, GameTeam>)
                typeof(TeamManager).GetField("_activeTeams", Fields)!.GetValue(Teams)!;

        public void Dispose()
        {
            foreach (var (_, field, previous) in _slots)
                field.SetValue(null, previous);
            SingletonContainer.ServiceProvider = null;
        }
    }

    private static Character Connect(uint id, string name)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = name };
        character.Connection = new GameConnection(new RecordingSession()) { ActiveChar = character };
        character.IsOnline = true;
        return character;
    }

    private static GameTeam BuildRaid(Character owner, params Character[] members)
    {
        var team = new GameTeam { Id = RaidTeamId, OwnerId = owner.Id, IsParty = false };
        team.AddMember(owner);
        foreach (var member in members)
            team.AddMember(member);
        return team;
    }

    [Test]
    public async Task IsOnlineFalse_ReachesTheJointManagerThroughTeamManagerSetOffline()
    {
        using var fixture = new Fixture();
        var alice = Connect(Alice, "Alice");
        var carol = Connect(Carol, "Carol");
        fixture.World.AddCharacter(Alice, "Alice").AddCharacter(Carol, "Carol");
        fixture.World.AddTeam(RaidTeamId, Alice, false, Alice, Carol);
        fixture.ActiveTeams[RaidTeamId] = BuildRaid(alice, carol);

        // an open summon round addressed to Carol
        fixture.Joints.RequestSummons(Alice);
        await Assert.That(fixture.Joints.PendingSummonCount).IsEqualTo(1);

        // the production path: the online flag flipping is what calls SetOffline
        carol.IsOnline = false;

        await Assert.That(fixture.Joints.PendingSummonCount).IsEqualTo(0);
        await Assert.That(fixture.Joints.ReplyToSummon(Carol, true, "Alice")).IsFalse();
    }

    [Test]
    public async Task IsOnlineFalse_OfTheRequestingOwnerCancelsTheirPendingJointRequest()
    {
        using var fixture = new Fixture();
        var alice = Connect(Alice, "Alice");
        var carol = Connect(Carol, "Carol");
        fixture.World.AddCharacter(Alice, "Alice").AddCharacter(Carol, "Carol");
        fixture.World.AddTeam(RaidTeamId, Alice, false, Alice, Carol);
        fixture.ActiveTeams[RaidTeamId] = BuildRaid(alice, carol);

        // Carol's raid is the only one available, so the request targets a member of the same team
        // and is refused; what matters here is that Alice's own request disappears with her.
        fixture.Joints.RequestJointInfo(Alice, 1UL, TeamJointModes.ContextRequest, "Carol", 1);
        await Assert.That(fixture.Joints.PendingJointCount).IsEqualTo(0);

        // give Alice a second raid to target so a real request exists
        fixture.World.AddCharacter(2u, "Bob").AddTeam(200u, 2u, false, 2u);
        fixture.Joints.RequestJointInfo(Alice, 1UL, TeamJointModes.ContextRequest, "Bob", 1);
        await Assert.That(fixture.Joints.PendingJointCount).IsEqualTo(1);

        carol.IsOnline = false;  // a plain member must not cancel Alice's request
        await Assert.That(fixture.Joints.PendingJointCount).IsEqualTo(1);

        alice.IsOnline = false;  // the requester cancels her own
        await Assert.That(fixture.Joints.PendingJointCount).IsEqualTo(0);
    }
}
