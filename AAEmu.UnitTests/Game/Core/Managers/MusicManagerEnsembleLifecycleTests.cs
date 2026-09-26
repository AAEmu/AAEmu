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
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Music;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class MusicManagerEnsembleLifecycleTests
{
    private const uint Maestro = 0x401;
    private const uint Member = 0x402;
    private const uint Second = 0x403;
    private const uint Invited = 0x404;
    private const uint SecondMaestro = 0x405;

    [Test]
    public async Task Disconnect_RemovesAStartedSessionAndClosesOnlyTheRemainingPlayers()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var second = environment.NewCharacter(Second, "Second");
        var session = StartedSession(maestro.ObjId, member.ObjId, second.ObjId);
        environment.AddSession(session);

        environment.Manager.OnCharacterLogout(member);

        await Assert.That(environment.Track(session)).IsFalse();
        await Assert.That(session.IsCanceled).IsTrue();
        await Assert.That(environment.Opcodes(member.ObjId)).IsEmpty();
        await AssertOpcodes(environment.Opcodes(maestro.ObjId), SCOffsets.SCEnsembleCanceledPacket);
        await AssertOpcodes(environment.Opcodes(second.ObjId), SCOffsets.SCEnsembleCanceledPacket);
    }

    [Test]
    public async Task InvitedDisconnect_RemovesTheInvitationWithoutBroadcastingAMemberLeave()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var invited = environment.NewCharacter(Invited, "Invited");
        var session = StartedSession(maestro.ObjId, member.ObjId);
        session.Invite(invited.ObjId);
        environment.AddSession(session);

        environment.Manager.OnCharacterLogout(invited);

        await Assert.That(environment.Track(session)).IsTrue();
        await Assert.That(session.IsStarted).IsTrue();
        await Assert.That(session.Invited).IsEmpty();
        await Assert.That(environment.Opcodes(invited.ObjId)).IsEmpty();
        await Assert.That(environment.Opcodes(maestro.ObjId)).IsEmpty();
        await Assert.That(environment.Opcodes(member.ObjId)).IsEmpty();
    }

    [Test]
    public async Task OpenMemberLeave_DeletesThePartThenRefreshesOnlyTheRemainingRoster()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var second = environment.NewCharacter(Second, "Second");
        var session = OpenSession(maestro.ObjId, member.ObjId, second.ObjId);
        environment.AddSession(session);

        environment.Manager.LeaveEnsemble(member);

        var expected = new[] { SCOffsets.SCDeleteEnsembleSoundPacket, SCOffsets.SCEnsembleStartedPacket };
        await Assert.That(environment.Opcodes(member.ObjId)).IsEmpty();
        await AssertOpcodes(environment.Opcodes(maestro.ObjId), expected);
        await AssertOpcodes(environment.Opcodes(second.ObjId), expected);
    }

    [Test]
    public async Task StartedMemberLeave_DeletesOnlyThatPartAndKeepsThePerformance()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var second = environment.NewCharacter(Second, "Second");
        var session = StartedSession(maestro.ObjId, member.ObjId, second.ObjId);
        environment.AddSession(session);

        environment.Manager.LeaveEnsemble(member);

        await Assert.That(environment.Opcodes(member.ObjId)).IsEmpty();
        await AssertOpcodes(environment.Opcodes(maestro.ObjId), SCOffsets.SCDeleteEnsembleSoundPacket);
        await AssertOpcodes(environment.Opcodes(second.ObjId), SCOffsets.SCDeleteEnsembleSoundPacket);
        await Assert.That(session.IsStarted).IsTrue();
        await Assert.That(session.IsCanceled).IsFalse();
    }

    [Test]
    public async Task MaestroLeave_CancelsTheCapturedParticipantsOnceEach()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var second = environment.NewCharacter(Second, "Second");
        var session = StartedSession(maestro.ObjId, member.ObjId, second.ObjId);
        environment.AddSession(session);

        environment.Manager.LeaveEnsemble(maestro);

        await Assert.That(environment.Track(session)).IsFalse();
        await Assert.That(session.IsCanceled).IsTrue();
        await AssertOpcodes(environment.Opcodes(maestro.ObjId), SCOffsets.SCEnsembleCanceledPacket);
        await AssertOpcodes(environment.Opcodes(member.ObjId), SCOffsets.SCEnsembleCanceledPacket);
        await AssertOpcodes(environment.Opcodes(second.ObjId), SCOffsets.SCEnsembleCanceledPacket);
    }

    [Test]
    public async Task PartRouting_RejectsWrongSenderMaestroAndSizeBeforeMutation()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var session = OpenSession(maestro.ObjId, member.ObjId);
        environment.AddSession(session);
        var data = new byte[] { 0x4D, 0x54, 0x00, 0xFF };
        const uint size = 4;

        await Assert.That(environment.Manager.EnsemblePartReady(member, Second, Maestro, size, data)).IsFalse();
        await Assert.That(environment.Manager.EnsemblePartReady(member, Member, Second, size, data)).IsFalse();
        await Assert.That(environment.Manager.EnsemblePartReady(member, Member, Maestro, size + 1, data)).IsFalse();
        await Assert.That(session.Parts).IsEmpty();
        await Assert.That(environment.Opcodes(maestro.ObjId)).IsEmpty();
    }

    [Test]
    public async Task PartRouting_ForwardsAValidatedRawMidiPartToTheMaestro()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var session = OpenSession(maestro.ObjId, member.ObjId);
        environment.AddSession(session);
        var data = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00 };
        const uint size = 6;

        await Assert.That(environment.Manager.EnsemblePartReady(member, Member, Maestro, size, data)).IsTrue();

        await Assert.That(session.Parts.Contains(Member)).IsTrue();
        await Assert.That(environment.Opcodes(member.ObjId)).IsEmpty();
        await AssertOpcodes(environment.Opcodes(maestro.ObjId), SCOffsets.SCEnsembleMidiBinReadyPacket);

        var packet = environment.Packet(maestro.ObjId);
        await Assert.That(ReadBc(packet, 8)).IsEqualTo(Member);
        await Assert.That(ReadBc(packet, 11)).IsEqualTo(Maestro);
        await Assert.That(BitConverter.ToUInt32(packet, 14)).IsEqualTo(size);
        // The payload follows the serializer's length-prefixed blob slot, so a u16 block length sits
        // between the declared size and the bytes.
        await Assert.That(BitConverter.ToUInt16(packet, 18)).IsEqualTo((ushort)size);
        await Assert.That(packet.Skip(20).Take((int)size)).IsEquivalentTo(data);
    }

    [Test]
    public async Task PartRouting_UsesTheClaimedMaestroWhenAMemberHasTwoSessions()
    {
        using var environment = new EnsembleEnvironment();
        var firstMaestro = environment.NewCharacter(Maestro, "FirstMaestro");
        var secondMaestro = environment.NewCharacter(SecondMaestro, "SecondMaestro");
        var member = environment.NewCharacter(Member, "Member");
        var firstSession = OpenSession(firstMaestro.ObjId, member.ObjId);
        var secondSession = OpenSession(secondMaestro.ObjId, member.ObjId);
        environment.AddSession(firstSession);
        environment.AddSession(secondSession);
        var data = new byte[] { 0x4D, 0x54, 0x00, 0xFF };
        const uint size = 4;

        await Assert.That(environment.Manager.EnsemblePartReady(member, Member, SecondMaestro, size, data)).IsTrue();

        await Assert.That(firstSession.Parts).IsEmpty();
        await Assert.That(secondSession.Parts.Contains(Member)).IsTrue();
        await Assert.That(environment.Opcodes(firstMaestro.ObjId)).IsEmpty();
        await AssertOpcodes(environment.Opcodes(secondMaestro.ObjId), SCOffsets.SCEnsembleMidiBinReadyPacket);
    }

    [Test]
    public async Task EnsembleRegistry_SerializesConcurrentLookupsAndPartRoutes()
    {
        using var environment = new EnsembleEnvironment();
        var maestro = environment.NewCharacter(Maestro, "Maestro");
        var member = environment.NewCharacter(Member, "Member");
        var session = OpenSession(maestro.ObjId, member.ObjId);
        environment.AddSession(session);
        var data = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00 };
        const uint size = 6;

        Parallel.For(0, 128, _ =>
        {
            environment.Manager.FindEnsemble(member.ObjId);
            environment.Manager.EnsemblePartReady(member, Member, Maestro, size, data);
        });

        await Assert.That(session.Parts.Contains(Member)).IsTrue();
        await Assert.That(environment.Opcodes(member.ObjId)).IsEmpty();
    }

    private static async Task AssertOpcodes(ushort[] actual, params ushort[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var i = 0; i < expected.Length; i++)
            await Assert.That(actual[i]).IsEqualTo(expected[i]);
    }

    private static EnsembleSession OpenSession(uint maestro, params uint[] members)
    {
        var session = new EnsembleSession(maestro, "Maestro");
        foreach (var member in members)
        {
            session.Invite(member);
            session.Accept(member);
        }
        return session;
    }

    private static EnsembleSession StartedSession(uint maestro, params uint[] members)
    {
        var session = OpenSession(maestro, members);
        foreach (var member in members)
            session.PartReady(member);
        session.PartReady(maestro);
        session.Start();
        return session;
    }

    private sealed class EnsembleEnvironment : IDisposable
    {
        private readonly Dictionary<uint, RecordingSession> _recordings = [];
        private readonly ConcurrentDictionary<uint, Character> _characters;
        private readonly object _previousWorld;

        public EnsembleEnvironment()
        {
            Manager = new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object);
            World = EmptyWorldManager();
            _previousWorld = SwapSingleton(World);
            _characters = (ConcurrentDictionary<uint, Character>)typeof(WorldManager)
                .GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(World)!;
        }

        public MusicManager Manager { get; }

        public WorldManager World { get; }

        public CharacterMock NewCharacter(uint id, string name)
        {
            var recording = new RecordingSession(id);
            var character = new CharacterMock
            {
                Id = id,
                ObjId = id,
                Name = name,
                Buffs = null,
            };
            character.Connection = new GameConnection(recording) { ActiveChar = character };
            _recordings[id] = recording;
            _characters[id] = character;
            return character;
        }

        public void AddSession(EnsembleSession session) =>
            ((Dictionary<uint, EnsembleSession>)typeof(MusicManager)
                .GetField("_ensembles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Manager)!).Add(session.MaestroBc, session);

        public bool Track(EnsembleSession session) =>
            ((Dictionary<uint, EnsembleSession>)typeof(MusicManager)
                .GetField("_ensembles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Manager)!).ContainsKey(session.MaestroBc);

        public ushort[] Opcodes(uint characterId) =>
            _recordings[characterId].Packets.Select(packet => BitConverter.ToUInt16(packet, 6)).ToArray();

        public byte[] Packet(uint characterId) => _recordings[characterId].Packets.Single();

        public void Dispose()
        {
            _characters.Clear();
            RestoreSingleton<WorldManager>(_previousWorld);
        }
    }

    private sealed class RecordingSession(uint sessionId) : ISession
    {
        private readonly object _packetLock = new();
        private readonly List<byte[]> _packets = [];

        public IReadOnlyList<byte[]> Packets
        {
            get
            {
                lock (_packetLock)
                    return _packets.ToArray();
            }
        }

        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => sessionId;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet)
        {
            lock (_packetLock)
                _packets.Add(packet);
        }

        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    private static uint ReadBc(byte[] packet, int offset) =>
        (uint)(packet[offset] | packet[offset + 1] << 8 | packet[offset + 2] << 16);

    private static WorldManager EmptyWorldManager() => new(
        Mock.Of<ITickManager>().Object,
        Mock.Of<IWorldIdManager>().Object,
        new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
        new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
        new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));

    private static object SwapSingleton<T>(T replacement) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        field.SetValue(null, replacement);
        return previous;
    }

    private static void RestoreSingleton<T>(object previous) where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, previous);
}
