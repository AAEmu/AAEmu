using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EnterWorldManagerTests
{
    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public bool Closed { get; private set; }
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() => Closed = true;
    }

    private static EnterWorldManager CreateManager() => new(
        Mock.Of<IAccountManager>().Object,
        Mock.Of<IStreamManager>().Object,
        Mock.Of<IQuestManager>().Object,
        Mock.Of<ITeamManager>().Object,
        Mock.Of<IChatManager>().Object,
        Mock.Of<IFamilyManager>().Object,
        Mock.Of<IWorldManager>().Object);

    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockAccount = Mock.Of<IAccountManager>();
        var mockStream = Mock.Of<IStreamManager>();
        var mockQuest = Mock.Of<IQuestManager>();
        var mockTeam = Mock.Of<ITeamManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockFamily = Mock.Of<IFamilyManager>();
        var mockWorld = Mock.Of<IWorldManager>();

        var manager = new EnterWorldManager(
            mockAccount.Object,
            mockStream.Object,
            mockQuest.Object,
            mockTeam.Object,
            mockChat.Object,
            mockFamily.Object,
            mockWorld.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockAccount);
        Mock.VerifyNoOtherCalls(mockStream);
        Mock.VerifyNoOtherCalls(mockQuest);
        Mock.VerifyNoOtherCalls(mockTeam);
        Mock.VerifyNoOtherCalls(mockChat);
        Mock.VerifyNoOtherCalls(mockFamily);
        Mock.VerifyNoOtherCalls(mockWorld);
    }

    [Test]
    public async Task LeaveWorldTask_CharacterSelect_GrantsThenRestartsLobbyStateMachine()
    {
        var session = new RecordingSession();
        var connection = new GameConnection(session) { State = GameState.World };

        CreateManager().LeaveWorldTask(connection, LeaveWorldTargetType.CharacterSelect, null);

        await Assert.That(connection.State).IsEqualTo(GameState.Lobby);
        await Assert.That(session.Closed).IsFalse();
        await Assert.That(session.Packets.Count).IsEqualTo(2);

        // SCLeaveWorldGranted: [len][DD][level=1][crc][counter][opcode=0x003][target=CharacterSelect].
        var granted = session.Packets[0];
        await Assert.That(granted[2]).IsEqualTo((byte)0xdd);
        await Assert.That(granted[3]).IsEqualTo((byte)1);
        await Assert.That(BitConverter.ToUInt16(granted, 6)).IsEqualTo((ushort)0x003);
        await Assert.That(granted[8]).IsEqualTo((byte)LeaveWorldTargetType.CharacterSelect);

        // ChangeState(0): [len][DD][level=2][opcode=0x000][state=0].
        var changeState = session.Packets[1];
        await Assert.That(changeState[2]).IsEqualTo((byte)0xdd);
        await Assert.That(changeState[3]).IsEqualTo((byte)2);
        await Assert.That(BitConverter.ToUInt16(changeState, 4)).IsEqualTo((ushort)0x000);
        await Assert.That(BitConverter.ToInt32(changeState, 6)).IsEqualTo(0);
    }
}
