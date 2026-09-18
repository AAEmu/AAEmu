using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Squad;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class SquadPendingDisbandTests
{
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
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly FieldInfo _singleton = typeof(Singleton<WorldManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;
        public SquadManager Manager { get; } = new();
        public Dictionary<uint, Squad> Squads { get; }
        public Dictionary<uint, uint> Membership { get; }
        public ConcurrentDictionary<uint, Character> Online { get; }

        public Fixture()
        {
            Squads = (Dictionary<uint, Squad>)typeof(SquadManager).GetField("_squads", Fields)!.GetValue(Manager)!;
            Membership = (Dictionary<uint, uint>)typeof(SquadManager).GetField("_characterSquad", Fields)!.GetValue(Manager)!;
            var world = new WorldManager(null, null, null, null, null);
            Online = (ConcurrentDictionary<uint, Character>)typeof(WorldManager).GetField("_characters", Fields)!.GetValue(world)!;
            _previous = _singleton.GetValue(null);
            _singleton.SetValue(null, world);
        }

        public Squad AddSquad(uint id, params Character[] members)
        {
            var squad = new Squad { Id = id, LeaderCharacterId = members[0].Id };
            foreach (var character in members)
            {
                squad.Members.Add(new SquadMember { CharacterId = character.Id, IsLeader = character.Id == squad.LeaderCharacterId });
                Membership.Add(character.Id, id);
            }
            Squads.Add(id, squad);
            return squad;
        }

        public void Dispose() => _singleton.SetValue(null, _previous);
    }

    private static (Character Character, RecordingSession Session) CreateCharacter(uint id)
    {
        var session = new RecordingSession();
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = $"Test{id}" };
        character.Connection = new GameConnection(session) { ActiveChar = character };
        return (character, session);
    }

    private static async Task AssertPackets(RecordingSession session, params ushort[] opcodes)
    {
        await Assert.That(session.Packets.Count).IsEqualTo(opcodes.Length);
        for (var i = 0; i < opcodes.Length; i++)
        {
            await Assert.That(BitConverter.ToUInt16(session.Packets[i], 6)).IsEqualTo(opcodes[i]);
            if (opcodes[i] == SCOffsets.SCCancelInstantGamePacket)
                await Assert.That(BitConverter.ToUInt16(session.Packets[i], 8)).IsEqualTo((ushort)0);
        }
    }

    [Test]
    [Arguments(false, false, true)] // No world character (disconnected or at character select).
    [Arguments(true, true, true)] // Presence changed before removal from WorldManager.
    [Arguments(true, false, false)] // World entry remains, but its connection is unavailable.
    public async Task DisbandWhileUnavailable_NotifiesOnlyThatMemberOnceOnLogin(bool inWorld, bool offline, bool connected)
    {
        using var fixture = new Fixture();
        var (leader, leaderSession) = CreateCharacter(1007);
        var (member, memberSession) = CreateCharacter(1008);
        var (unrelated, unrelatedSession) = CreateCharacter(1009);
        fixture.Online[leader.ObjId] = leader;
        if (inWorld)
            fixture.Online[member.ObjId] = member;
        if (!connected)
            member.Connection = null;
        var squad = fixture.AddSquad(1, leader, member);
        squad.GetMember(member.Id).Offline = offline;

        fixture.Manager.Disband(leader);
        await AssertPackets(leaderSession, SCOffsets.SCDisbandSquadPacket);
        await AssertPackets(memberSession);
        await Assert.That(fixture.Squads).IsEmpty();
        await Assert.That(fixture.Membership).IsEmpty();
        fixture.Manager.SyncClientSquadAfterLogin(unrelated);
        await AssertPackets(unrelatedSession, SCOffsets.SCCancelInstantGamePacket);

        // A new Character instance with the same persistent Id must receive the pending event.
        var (reconnected, loginSession) = CreateCharacter(member.Id);
        fixture.Online[reconnected.ObjId] = reconnected;
        fixture.Manager.SyncClientSquadAfterLogin(reconnected);
        fixture.Manager.SyncClientSquadAfterLogin(reconnected);
        await AssertPackets(loginSession, SCOffsets.SCDisbandSquadPacket,
            SCOffsets.SCCancelInstantGamePacket, SCOffsets.SCCancelInstantGamePacket);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PendingDisband_DoesNotClearNewSquadOrLeakToALaterLogin(bool syncBeforeDisband)
    {
        using var fixture = new Fixture();
        var (leader, _) = CreateCharacter(1007);
        var (member, memberSession) = CreateCharacter(1008);
        fixture.Online[leader.ObjId] = leader;
        fixture.AddSquad(1, leader, member);
        fixture.Manager.Disband(leader);

        fixture.Online[member.ObjId] = member;
        fixture.AddSquad(2, member);
        if (syncBeforeDisband)
            fixture.Manager.SyncClientSquadAfterLogin(member);
        await AssertPackets(memberSession);
        await Assert.That(fixture.Membership[member.Id]).IsEqualTo(2u);
        fixture.Manager.Disband(member);
        await AssertPackets(memberSession, SCOffsets.SCDisbandSquadPacket);
        memberSession.Packets.Clear();
        fixture.Manager.SyncClientSquadAfterLogin(member);
        await AssertPackets(memberSession, SCOffsets.SCCancelInstantGamePacket);
    }

    [Test]
    public async Task AnotherOfflineDisband_AfterConsumption_IsDeliveredAgain()
    {
        using var fixture = new Fixture();
        var (leader, _) = CreateCharacter(1007);
        var (member, session) = CreateCharacter(1008);
        fixture.Online[leader.ObjId] = leader;
        for (uint id = 1; id <= 2; id++)
        {
            fixture.AddSquad(id, leader, member);
            fixture.Manager.Disband(leader);
            fixture.Manager.SyncClientSquadAfterLogin(member);
            await AssertPackets(session, SCOffsets.SCDisbandSquadPacket, SCOffsets.SCCancelInstantGamePacket);
            session.Packets.Clear();
        }
    }
}
