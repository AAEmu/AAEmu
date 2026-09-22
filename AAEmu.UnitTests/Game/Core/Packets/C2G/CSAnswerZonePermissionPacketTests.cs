using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The zone-permission request (CS 0x058) judged against the ask the server opened: only a character
/// holding one may settle permission, and every answer — accepted, declined, unsolicited or malformed —
/// leaves the client with a definitive packet.
/// </summary>
[NotInParallel]
public sealed class CSAnswerZonePermissionPacketTests
{
    private const uint CharacterId = 71_001;
    private const uint ZoneGroupId = 955;

    private readonly List<byte[]> _sent = [];
    private readonly GameConnection _connection;
    private readonly Character _character;
    private readonly SingletonScope<IndunManager> _indun;

    public CSAnswerZonePermissionPacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));
        _connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams())
        {
            Id = CharacterId,
            ObjId = CharacterId,
            Name = "Asked",
            Connection = _connection,
        };
        _connection.Characters.Add(_character.Id, _character);
        _connection.ActiveChar = _character;

        _indun = new SingletonScope<IndunManager>(new IndunManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<ITeamManager>().Object));
    }

    [After(Test)]
    public void ReleaseManager() => _indun.Dispose();

    [Test]
    public async Task AcceptedAnswer_WithOpenAsk_SettlesItAndAnswersWithTheRefresh()
    {
        await Assert.That(IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId)).IsTrue();

        var mark = _sent.Count;
        Answer(1);

        await Assert.That(_sent.Count).IsEqualTo(mark + 1);
        var (opcode, body) = SentPacket.Read(_sent[mark]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCZonePermissionChangedPacket);
        await Assert.That(body.Length).IsEqualTo(0);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task DeclinedAnswer_WithOpenAsk_StillGetsADefinitiveResultAndClosesTheAsk()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);

        var mark = _sent.Count;
        Answer(0);

        await Assert.That(_sent.Count).IsEqualTo(mark + 1);
        var (opcode, body) = SentPacket.Read(_sent[mark]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCZonePermissionChangedPacket);
        await Assert.That(body.Length).IsEqualTo(0);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task AnswerWithoutAnOpenAsk_IsRefusedWithAnErrorAndChangesNothing()
    {
        var mark = _sent.Count;
        Answer(1);

        await Assert.That(_sent.Count).IsEqualTo(mark + 1);
        await AssertError(mark, ErrorMessageType.InvalidStateInstance);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task SecondAnswer_AfterTheAskSettled_IsRefusedWithAnError()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);
        Answer(1);

        var mark = _sent.Count;
        Answer(1);

        await Assert.That(_sent.Count).IsEqualTo(mark + 1);
        await AssertError(mark, ErrorMessageType.InvalidStateInstance);
    }

    [Test]
    public async Task MalformedAnswer_FailsLoudAndLeavesTheAskForALegitimateAnswer()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);

        var mark = _sent.Count;
        Answer(7);

        // Garbage never settles permission, and it never burns the ask either: the dialog can only
        // produce 0 or 1, so anything else is refused without touching the state it names.
        await Assert.That(_sent.Count).IsEqualTo(mark + 1);
        await AssertError(mark, ErrorMessageType.InvalidStateInstance);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsTrue();

        mark = _sent.Count;
        Answer(1);
        await Assert.That(_sent.Count).IsEqualTo(mark + 1);
        var (opcode, _) = SentPacket.Read(_sent[mark]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCZonePermissionChangedPacket);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task OpeningAnAskForNoZoneGroup_IsRefusedAndOpensNothing()
    {
        await Assert.That(IndunManager.Instance.OpenZonePermissionAsk(_character, 0)).IsFalse();
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task AnswerWithoutAnActiveCharacter_SendsNothingAndDoesNotThrow()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);
        _connection.ActiveChar = null;

        var mark = _sent.Count;
        Answer(1);

        await Assert.That(_sent.Count).IsEqualTo(mark);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsTrue();
    }

    private void Answer(byte value) =>
        new CSAnswerZonePermissionPacket { Connection = _connection }
            .Read(new PacketStream().Write(value));

    private async Task AssertError(int index, ErrorMessageType expected)
    {
        var (opcode, body) = SentPacket.Read(_sent[index]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCErrorMsgPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)expected);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)expected);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
